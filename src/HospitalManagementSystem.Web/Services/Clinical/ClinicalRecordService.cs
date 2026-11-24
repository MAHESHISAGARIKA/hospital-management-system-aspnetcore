using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Clinical;

public sealed class ClinicalWorkflowException(string message) : Exception(message);
public sealed class ClinicalAccessException : Exception;

public sealed class ClinicalRecordService(HospitalDbContext db, IAuditLogService audit)
{
    public static DateOnly LocalToday => DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow));

    public async Task<ClinicalOverview> OverviewAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounters = await Permitted(doctor).AsNoTracking().Include(e => e.Patient)
            .OrderByDescending(e => e.CreatedAtUtc).ToArrayAsync(ct);
        var visits = await db.Appointments.AsNoTracking().Include(a => a.Patient)
            .Where(a => a.DoctorId == doctor && a.Patient.IsActive && a.Status == AppointmentStatus.CheckedIn &&
                !db.ClinicalEncounters.Any(e => e.AppointmentId == a.Id))
            .OrderByDescending(a => a.StartAtUtc).ToArrayAsync(ct);
        await audit.WriteAsync("ClinicalOverviewViewed", actor, null, $"Doctor {doctor:D}", ct);
        return new(visits, encounters);
    }

    public async Task<Guid> OpenAsync(ClaimsPrincipal user, Guid appointmentId, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var visit = await db.Appointments.AsNoTracking().Include(a => a.Patient)
            .SingleOrDefaultAsync(a => a.Id == appointmentId && a.DoctorId == doctor &&
                a.Patient.IsActive && a.Status == AppointmentStatus.CheckedIn, ct);
        if (visit is null)
            throw await DeniedAsync(actor, "Appointment", appointmentId, ct);
        if (await db.ClinicalEncounters.AnyAsync(e => e.AppointmentId == appointmentId, ct))
            throw new ClinicalWorkflowException("An encounter already exists for this visit.");

        var now = DateTime.UtcNow;
        var encounter = new ClinicalEncounter
        {
            Id = Guid.CreateVersion7(), PatientId = visit.PatientId, DoctorId = doctor, AppointmentId = appointmentId,
            CreatedByUserId = actor, UpdatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now,
            ConcurrencyToken = Guid.CreateVersion7()
        };
        db.ClinicalEncounters.Add(encounter);
        await SaveAsync("ClinicalEncounterOpened", actor, "Encounter", encounter.Id, "Open", ct);
        return encounter.Id;
    }

    public async Task<ClinicalReport> EncounterAsync(ClaimsPrincipal user, Guid id, bool printable, Guid? recordId, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        if (recordId.HasValue && !encounter.Records.Any(record => record.Id == recordId && record.Kind == ClinicalRecordKind.Prescription))
            throw await DeniedAsync(actor, "Prescription", recordId, ct);
        await audit.WriteAsync(printable ? "ClinicalReportGenerated" : "ClinicalEncounterViewed", actor, null,
            recordId.HasValue ? $"Prescription {recordId:D}" : $"Encounter {id:D}", ct);
        return new(recordId.HasValue ? "Prescription" : printable ? "Encounter report" : "Clinical encounter",
            [encounter], DateTime.UtcNow, printable, recordId);
    }

    public async Task<ClinicalReport> PatientAsync(ClaimsPrincipal user, Guid id, DateOnly? from, DateOnly? to, Guid? encounterId, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        if (from > to)
            throw new ClinicalWorkflowException("The end date must be on or after the start date.");
        var encounters = await WithClinicalData(Permitted(doctor).AsNoTracking())
            .Where(e => e.PatientId == id && (!encounterId.HasValue || e.Id == encounterId))
            .OrderByDescending(e => e.CreatedAtUtc).ToArrayAsync(ct);
        if (encounters.Length == 0)
            throw await DeniedAsync(actor, "Patient", id, ct);
        await audit.WriteAsync("ClinicalTimelineViewed", actor, null, $"Patient {id:D}", ct);
        return new("Patient clinical timeline", encounters, DateTime.UtcNow, false, From: from, To: to, EncounterId: encounterId);
    }

    public async Task<ClinicalEditor> EditorAsync(ClaimsPrincipal user, Guid id, Guid? recordId, ClinicalRecordKind kind, bool amend, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        var record = await RecordAsync(encounter, recordId, actor, ct);
        EnsureEditable(encounter, record, amend);
        await audit.WriteAsync("ClinicalEditorViewed", actor, null, $"Encounter {id:D}", ct);
        return new(id, recordId, amend, new ClinicalEntryInput
        {
            EncounterToken = encounter.ConcurrencyToken, RecordToken = record?.ConcurrencyToken ?? Guid.Empty,
            Kind = record?.Kind ?? kind, RecordedDate = record?.RecordedDate ?? LocalToday,
            Content = record?.Content, Code = record?.Code, Notes = record?.Notes,
            Medicines = record?.Items.OrderBy(i => i.Position).Select(i => new MedicineInput
            {
                Id = amend ? null : i.Id, Medicine = i.Medicine, Strength = i.Strength, Dose = i.Dose,
                DoseUnit = i.DoseUnit, Route = i.Route, Frequency = i.Frequency, Duration = i.Duration,
                Quantity = i.Quantity, Instructions = i.Instructions
            }).ToList() ?? []
        });
    }

    public async Task SaveRecordAsync(ClaimsPrincipal user, Guid id, Guid? recordId, bool amend, ClinicalEntryInput input, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        var original = await RecordAsync(encounter, recordId, actor, ct);
        EnsureEditable(encounter, original, amend);
        EnsureCurrent(encounter, input.EncounterToken, original, input.RecordToken);

        var kind = original?.Kind ?? input.Kind;
        var medicines = Validate(kind, input);
        var reason = amend ? RequireReason(input.AmendmentReason) : null;
        var now = DateTime.UtcNow;
        var record = !amend && original is not null ? original : new ClinicalRecord
        {
            Id = Guid.CreateVersion7(), EncounterId = id, Kind = kind, CreatedAtUtc = now, CreatedByUserId = actor,
            PreviousRevisionId = amend ? original!.Id : null, AmendmentReason = reason
        };
        if (medicines.Any(input => input.Id.HasValue && !record.Items.Any(item => item.Id == input.Id)))
            throw new ClinicalWorkflowException("A medicine line no longer belongs to this draft.");

        if (record != original)
            db.ClinicalRecords.Add(record);
        record.RecordedDate = input.RecordedDate;
        record.Content = kind == ClinicalRecordKind.Prescription ? null : Normalize(input.Content);
        record.Code = kind == ClinicalRecordKind.Diagnosis ? Normalize(input.Code) : null;
        record.Notes = Normalize(input.Notes);
        ReplaceMedicines(record, medicines);
        Touch(encounter, actor, now);
        Touch(record, actor, now);
        await SaveAsync(amend ? "ClinicalAmendmentCreated" : original is null ? "ClinicalRecordCreated" : "ClinicalDraftUpdated",
            actor, kind.ToString(), record.Id, "Draft", ct);
    }

    public async Task FinalizeRecordAsync(ClaimsPrincipal user, Guid id, Guid recordId, ClinicalTransitionInput input, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        var record = (await RecordAsync(encounter, recordId, actor, ct))!;
        EnsureCurrent(encounter, input.EncounterToken, record, input.RecordToken);
        if (record.Status != ClinicalStatus.Draft)
            throw new ClinicalWorkflowException("Only draft entries can be finalized.");

        var now = DateTime.UtcNow;
        record.Status = ClinicalStatus.Finalized;
        record.FinalizedAtUtc = now;
        record.FinalizedByUserId = actor;
        Touch(encounter, actor, now);
        Touch(record, actor, now);
        await SaveAsync("ClinicalRecordFinalized", actor, record.Kind.ToString(), record.Id, "Draft to Finalized", ct);
    }

    public async Task VoidPrescriptionAsync(ClaimsPrincipal user, Guid id, Guid recordId, ClinicalTransitionInput input, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        var original = (await RecordAsync(encounter, recordId, actor, ct))!;
        EnsureCurrent(encounter, input.EncounterToken, original, input.RecordToken);
        EnsureEditable(encounter, original, true);
        if (original.Kind != ClinicalRecordKind.Prescription)
            throw new ClinicalWorkflowException("Only prescriptions support this void action.");
        var reason = RequireReason(input.AmendmentReason);
        var now = DateTime.UtcNow;
        var correction = new ClinicalRecord
        {
            Id = Guid.CreateVersion7(), EncounterId = id, Kind = original.Kind, PreviousRevisionId = original.Id,
            AmendmentReason = reason, Status = ClinicalStatus.Voided, RecordedDate = original.RecordedDate,
            CreatedAtUtc = now, CreatedByUserId = actor, FinalizedAtUtc = now, FinalizedByUserId = actor
        };
        Touch(encounter, actor, now);
        Touch(correction, actor, now);
        db.ClinicalRecords.Add(correction);
        await SaveAsync("ClinicalPrescriptionVoided", actor, "Prescription", correction.Id, "Finalized to Voided", ct);
    }

    public async Task FinalizeEncounterAsync(ClaimsPrincipal user, Guid id, Guid token, CancellationToken ct)
    {
        var (actor, doctor) = await CurrentDoctorAsync(user, ct);
        var encounter = await LoadAsync(actor, doctor, id, ct);
        EnsureCurrent(encounter, token);
        if (encounter.FinalizedAtUtc.HasValue || encounter.Records.Any(r => r.Status == ClinicalStatus.Draft))
            throw new ClinicalWorkflowException("Finalize all draft entries before closing an open encounter.");
        var now = DateTime.UtcNow;
        encounter.FinalizedAtUtc = now;
        encounter.FinalizedByUserId = actor;
        Touch(encounter, actor, now);
        await SaveAsync("ClinicalEncounterFinalized", actor, "Encounter", id, "Open to Finalized", ct);
    }

    private IQueryable<ClinicalEncounter> Permitted(Guid doctor)
        => db.ClinicalEncounters.Where(e => e.DoctorId == doctor && e.Patient.IsActive && e.Doctor.IsActive &&
            e.Appointment != null && e.Appointment.DoctorId == doctor && e.Appointment.PatientId == e.PatientId &&
            (e.Appointment.Status == AppointmentStatus.CheckedIn || e.Appointment.Status == AppointmentStatus.Completed));

    private static IQueryable<ClinicalEncounter> WithClinicalData(IQueryable<ClinicalEncounter> query)
        => query.Include(e => e.Patient).Include(e => e.Doctor).Include(e => e.Records).ThenInclude(record => record.Items);

    private async Task<ClinicalEncounter> LoadAsync(Guid actor, Guid doctor, Guid id, CancellationToken ct)
        => await WithClinicalData(Permitted(doctor)).SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Encounter", id, ct);

    private async Task<(Guid Actor, Guid Doctor)> CurrentDoctorAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole(HospitalRoles.Doctor) ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            throw new ClinicalAccessException();

        var doctor = await db.Doctors.AsNoTracking().Where(d => d.IsActive && d.ApplicationUserId == actor &&
                db.Users.Any(account => account.Id == actor && account.IsEnabled))
            .Select(d => (Guid?)d.Id).SingleOrDefaultAsync(ct);
        return (actor, doctor ?? throw await DeniedAsync(actor, "DoctorProfile", null, ct));
    }

    private async Task<ClinicalRecord?> RecordAsync(ClinicalEncounter encounter, Guid? id, Guid actor, CancellationToken ct)
        => !id.HasValue ? null : encounter.Records.SingleOrDefault(item => item.Id == id)
            ?? throw await DeniedAsync(actor, "ClinicalRecord", id, ct);

    private async Task<ClinicalAccessException> DeniedAsync(Guid actor, string type, Guid? id, CancellationToken ct)
    {
        await audit.WriteAsync("ClinicalAccessDenied", actor, null, $"{type} {id:D}", ct);
        return new ClinicalAccessException();
    }

    private async Task SaveAsync(string action, Guid actor, string type, Guid id, string transition, CancellationToken ct)
    {
        try
        {
            // The audit service saves the shared context once, atomically with the clinical changes.
            await audit.WriteAsync(action, actor, null, $"{type} {id:D} {transition}", ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ClinicalWorkflowException("This encounter changed. Reload it before trying again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" or "23514" or "23503" or "40P01" })
        {
            db.ChangeTracker.Clear();
            throw new ClinicalWorkflowException("The visit or record changed. Reload it before trying again.");
        }
    }

    private static void EnsureEditable(ClinicalEncounter encounter, ClinicalRecord? record, bool amend)
    {
        if (amend)
        {
            if (record?.Status != ClinicalStatus.Finalized || encounter.Records.Any(item => item.PreviousRevisionId == record.Id))
                throw new ClinicalWorkflowException("Only the latest finalized entry can be amended or voided.");
            return;
        }
        if (record is { Status: not ClinicalStatus.Draft } ||
            (encounter.FinalizedAtUtc.HasValue && record?.PreviousRevisionId is null))
            throw new ClinicalWorkflowException("Use an amendment to correct a finalized entry.");
    }

    private static void EnsureCurrent(ClinicalEncounter encounter, Guid token, ClinicalRecord? record = null, Guid recordToken = default)
    {
        if (encounter.ConcurrencyToken != token || (record is not null && record.ConcurrencyToken != recordToken))
            throw new ClinicalWorkflowException("This encounter changed. Reload it before trying again.");
    }

    private static List<MedicineInput> Validate(ClinicalRecordKind kind, ClinicalEntryInput input)
    {
        if (!Enum.IsDefined(kind) || input.RecordedDate == default || input.RecordedDate > LocalToday ||
            !Validator.TryValidateObject(input, new ValidationContext(input), null, true))
            throw new ClinicalWorkflowException("Enter a valid record date and check the field lengths.");
        if (kind != ClinicalRecordKind.Prescription)
        {
            if (Normalize(input.Content) is null)
                throw new ClinicalWorkflowException("Clinical description is required.");
            return [];
        }

        var medicines = input.Medicines.Where(item => item.HasContent).ToList();
        var ids = medicines.Where(item => item.Id.HasValue).Select(item => item.Id).ToArray();
        if (medicines.Count is < 1 or > 20 || ids.Distinct().Count() != ids.Length ||
            medicines.Any(item => !Validator.TryValidateObject(item, new ValidationContext(item), null, true) ||
                new[] { item.Medicine, item.Dose, item.DoseUnit, item.Route, item.Frequency, item.Duration }.Any(string.IsNullOrWhiteSpace)))
            throw new ClinicalWorkflowException("Enter 1 to 20 complete medicines with name, dose, unit, route, frequency and duration.");
        return medicines;
    }

    private void ReplaceMedicines(ClinicalRecord record, List<MedicineInput> medicines)
    {
        var original = record.Items.ToArray();
        foreach (var removed in original.Where(item => !medicines.Any(input => input.Id == item.Id)))
        {
            record.Items.Remove(removed);
            db.ClinicalPrescriptionItems.Remove(removed);
        }

        var position = original.Length == 0 ? 0 : original.Max(item => item.Position) + 1;
        foreach (var input in medicines)
        {
            var item = original.SingleOrDefault(item => item.Id == input.Id);
            if (item is null)
            {
                item = new PrescriptionItem { Id = Guid.CreateVersion7(), PrescriptionId = record.Id, Position = position++ };
                db.ClinicalPrescriptionItems.Add(item);
                if (!record.Items.Contains(item))
                    record.Items.Add(item);
            }
            item.Medicine = input.Medicine!.Trim();
            item.Strength = Normalize(input.Strength);
            item.Dose = input.Dose!.Trim();
            item.DoseUnit = input.DoseUnit!.Trim();
            item.Route = input.Route!.Trim();
            item.Frequency = input.Frequency!.Trim();
            item.Duration = input.Duration!.Trim();
            item.Quantity = input.Quantity;
            item.Instructions = Normalize(input.Instructions);
        }
    }

    private static void Touch(ClinicalEncounter encounter, Guid actor, DateTime now)
        => (encounter.UpdatedByUserId, encounter.UpdatedAtUtc, encounter.ConcurrencyToken) = (actor, now, Guid.CreateVersion7());

    private static void Touch(ClinicalRecord record, Guid actor, DateTime now)
        => (record.UpdatedByUserId, record.UpdatedAtUtc, record.ConcurrencyToken) = (actor, now, Guid.CreateVersion7());

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireReason(string? value) => Normalize(value) is { Length: <= 500 } reason
        ? reason : throw new ClinicalWorkflowException("Enter a correction reason of up to 500 characters.");
}
