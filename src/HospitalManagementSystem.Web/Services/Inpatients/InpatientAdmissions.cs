using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Inpatients;

public sealed partial class InpatientService
{
    public async Task<AdmissionEditor> EditorAsync(ClaimsPrincipal user, Guid? encounterId, AdmissionInput? input, CancellationToken ct)
    {
        if (encounterId.HasValue)
        {
            var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
            var encounter = await OriginAsync(actor, doctor, encounterId.Value, ct);
            return new(input ?? new AdmissionInput { EncounterId = encounterId, PatientId = encounter.PatientId,
                DoctorId = doctor, RequestKey = Guid.NewGuid() }, [encounter.Patient], [encounter.Doctor]);
        }
        await ActorAsync(user, ct, HospitalRoles.Receptionist);
        var patients = await db.Patients.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToArrayAsync(ct);
        var doctors = await db.Doctors.AsNoTracking().Where(d => d.IsActive && d.ApplicationUserId.HasValue &&
            db.Users.Any(u => u.Id == d.ApplicationUserId && u.IsEnabled)).OrderBy(d => d.LastName).ToArrayAsync(ct);
        return new(input ?? new AdmissionInput { RequestKey = Guid.NewGuid() }, patients, doctors);
    }

    public async Task<Guid> RegisterAsync(ClaimsPrincipal user, AdmissionInput input, CancellationToken ct)
    {
        Validate(input);
        if (input.RequestKey == Guid.Empty) throw new StayException("Reload the admission form.");
        var reason = Reason(input.Reason);
        Guid actor;
        if (input.EncounterId.HasValue)
        {
            var care = await clinical.CurrentDoctorAsync(user, ct);
            actor = care.Actor;
            var encounter = await OriginAsync(actor, care.Doctor, input.EncounterId.Value, ct);
            if (input.PatientId != encounter.PatientId || input.DoctorId != encounter.DoctorId)
                throw await DeniedAsync(actor, "Encounter", encounter.Id, ct);
        }
        else actor = await ActorAsync(user, ct, HospitalRoles.Receptionist);

        await using var transaction = await BeginAsync(ct);
        var key = $"admission:{actor:N}:{input.RequestKey:N}";
        await LockAsync($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS request_lock", ct);
        var prior = await db.InpatientAdmissions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CreatedByUserId == actor && x.RequestKey == input.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.PatientId == input.PatientId && prior.DoctorId == input.DoctorId &&
                prior.OriginEncounterId == input.EncounterId && prior.Reason == reason) return prior.Id;
            throw new StayException("This admission form has already been submitted with different details.");
        }
        if (!await db.Patients.AnyAsync(p => p.Id == input.PatientId && p.IsActive, ct) ||
            !await db.Doctors.AnyAsync(d => d.Id == input.DoctorId && d.IsActive &&
                db.Users.Any(u => u.Id == d.ApplicationUserId && u.IsEnabled), ct))
            throw new StayException("Choose an active patient and Doctor with an enabled linked account.");
        if (await db.InpatientAdmissions.AnyAsync(x =>
            (x.PatientId == input.PatientId && (x.Status == AdmissionStatus.Pending || x.Status == AdmissionStatus.Approved || x.Status == AdmissionStatus.Admitted)) ||
            (input.EncounterId.HasValue && x.OriginEncounterId == input.EncounterId && x.Status != AdmissionStatus.Cancelled), ct))
            throw new StayException("This patient already has an open admission or this consultation was already converted.");

        var admission = new InpatientAdmission { Id = Guid.CreateVersion7(), RequestKey = input.RequestKey, PatientId = input.PatientId,
            DoctorId = input.DoctorId, OriginEncounterId = input.EncounterId, Reason = reason, CreatedAtUtc = Now, CreatedByUserId = actor };
        admission.Number = $"ADM-{admission.Id:N}";
        Touch(admission, actor);
        db.InpatientAdmissions.Add(admission);
        await CommitAsync(transaction, "InpatientAdmissionRegistered", actor, $"Admission {admission.Id:D} Pending", ct);
        return admission.Id;
    }

    public async Task ApproveAsync(ClaimsPrincipal user, Guid id, AdmissionAction input, CancellationToken ct)
    {
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        await CheckDoctorAsync(admission, actor, doctor, ct);
        CheckToken(input.Token, admission.Token);
        if (admission.Status != AdmissionStatus.Pending || !admission.Patient.IsActive)
            throw new StayException("Only a pending admission for an active patient can be approved.");
        if (admission.OriginEncounterId.HasValue)
            await OriginAsync(actor, doctor, admission.OriginEncounterId.Value, ct);
        (admission.Status, admission.ApprovedAtUtc, admission.ApprovedByUserId) = (AdmissionStatus.Approved, Now, actor);
        Touch(admission, actor);
        await CommitAsync(tx, "InpatientAdmissionApproved", actor, $"Admission {id:D} Pending to Approved", ct);
    }

    public async Task CancelAsync(ClaimsPrincipal user, Guid id, AdmissionAction input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Receptionist, HospitalRoles.Doctor);
        var reason = Reason(input.Reason);
        var doctor = user.IsInRole(HospitalRoles.Receptionist) ? (Guid?)null : (await clinical.CurrentDoctorAsync(user, ct)).Doctor;
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        if (doctor.HasValue) await CheckDoctorAsync(admission, actor, doctor.Value, ct);
        CheckToken(input.Token, admission.Token);
        if (admission.Status is not (AdmissionStatus.Pending or AdmissionStatus.Approved))
            throw new StayException("Only pending or approved admissions can be cancelled.");
        var previous = admission.Status;
        (admission.Status, admission.CancelledAtUtc, admission.CancelledByUserId, admission.CancellationReason) =
            (AdmissionStatus.Cancelled, Now, actor, reason);
        Touch(admission, actor);
        await CommitAsync(tx, "InpatientAdmissionCancelled", actor, $"Admission {id:D} {previous} to Cancelled", ct);
    }

    public async Task AllocateAsync(ClaimsPrincipal user, Guid id, AdmissionAction input, bool transfer, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Receptionist);
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        CheckToken(input.Token, admission.Token);
        if (admission.Status != (transfer ? AdmissionStatus.Admitted : AdmissionStatus.Approved) ||
            !admission.Patient.IsActive || !admission.Doctor.IsActive || !await db.Users.AnyAsync(u =>
                u.Id == admission.Doctor.ApplicationUserId && u.IsEnabled && u.Id == admission.ApprovedByUserId, ct))
            throw new StayException("This admission is not ready for bed assignment.");
        var reason = transfer ? Reason(input.Reason) : null;
        var current = admission.Allocations.SingleOrDefault(x => x.EndedAtUtc == null);
        if (transfer && (current is null || current.BedId == input.BedId) || !transfer && current is not null)
            throw new StayException("Choose a different bed for transfer.");
        var destinationWard = await db.HospitalBeds.AsNoTracking().Where(x => x.Id == input.BedId)
            .Select(x => (Guid?)x.WardId).SingleOrDefaultAsync(ct);
        if (!destinationWard.HasValue || (input.WardId != Guid.Empty && input.WardId != destinationWard.Value))
            throw new StayException("Select a bed in the selected ward.");
        await LockBedsAsync(destinationWard.Value, input.BedId, current, ct);
        var destination = await Available(db.HospitalBeds).SingleOrDefaultAsync(x => x.Id == input.BedId, ct);
        if (destination is null)
            throw new StayException("The selected bed is no longer available. Choose another bed.");
        var at = current is null ? Now : EndTime(current);
        if (current is not null)
        {
            CloseAllocation(current, actor, at);
            if (db.Database.IsRelational())
            {
                // Close first so immediate PostgreSQL occupancy constraints see no overlapping allocation.
                try { await db.SaveChangesAsync(ct); }
                catch (Exception e) when (Conflict(e)) { db.ChangeTracker.Clear(); throw new StayException("The current bed changed. Reload the admission."); }
            }
        }
        else (admission.Status, admission.AdmittedByUserId, admission.AdmittedAtUtc) = (AdmissionStatus.Admitted, actor, at);

        var allocation = new BedAllocation { Id = Guid.CreateVersion7(), AdmissionId = id, BedId = destination.Id,
            StartedAtUtc = at, StartedByUserId = actor, TransferReason = reason };
        db.BedAllocations.Add(allocation);
        Touch(admission, actor);
        await CommitAsync(tx, transfer ? "InpatientBedTransferred" : "InpatientBedAssigned", actor,
            $"Admission {id:D} Bed {current?.BedId:D} to {destination.Id:D}", ct);
    }

    public async Task DischargeAsync(ClaimsPrincipal user, Guid id, DischargeInput input, CancellationToken ct)
    {
        Validate(input);
        if (input.Disposition is not { } disposition || !Enum.IsDefined(disposition)) throw new StayException("Select a valid discharge disposition.");
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        await CheckDoctorAsync(admission, actor, doctor, ct);
        CheckToken(input.Token, admission.Token);
        if (admission.Status != AdmissionStatus.Admitted || admission.Discharge is not null)
            throw new StayException("Only an active admission can be discharged.");
        var current = admission.Allocations.SingleOrDefault(x => x.EndedAtUtc == null)
            ?? throw new StayException("The admission does not have a current bed.");
        await LockBedsAsync(current.Bed.WardId, current.BedId, null, ct);
        var at = EndTime(current);
        CloseAllocation(current, actor, at);
        foreach (var assignment in admission.Assignments.Where(x => x.EndedAtUtc is null))
            (assignment.EndedAtUtc, assignment.EndedByUserId) = (at, actor);
        db.InpatientDischarges.Add(new InpatientDischarge { Id = Guid.CreateVersion7(), AdmissionId = id,
            Disposition = disposition, Diagnosis = input.Diagnosis.Trim(), Summary = input.Summary.Trim(), SignedAtUtc = at, SignedByUserId = actor });
        admission.Status = AdmissionStatus.Discharged;
        Touch(admission, actor);
        await CommitAsync(tx, "InpatientDischargeSigned", actor, $"Admission {id:D} Admitted to Discharged", ct);
    }

    private async Task<ClinicalEncounter> OriginAsync(Guid actor, Guid doctor, Guid id, CancellationToken ct)
        => await clinical.Permitted(doctor).AsNoTracking().Include(x => x.Patient).Include(x => x.Doctor)
            .SingleOrDefaultAsync(x => x.Id == id && x.AppointmentId.HasValue, ct) ?? throw await DeniedAsync(actor, "Encounter", id, ct);

    private async Task<InpatientAdmission> LoadAsync(Guid id, Guid actor, CancellationToken ct, bool track = true)
        => await (track ? db.InpatientAdmissions.AsTracking() : db.InpatientAdmissions.AsNoTracking())
            .Include(x => x.Patient).Include(x => x.Doctor).Include(x => x.Discharge).Include(x => x.Assignments)
            .Include(x => x.Observations).Include(x => x.Allocations).ThenInclude(x => x.Bed).ThenInclude(x => x.Ward)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw await DeniedAsync(actor, "Admission", id, ct);

    private async Task CheckDoctorAsync(InpatientAdmission admission, Guid actor, Guid doctor, CancellationToken ct)
    {
        if (admission.DoctorId != doctor || !admission.Patient.IsActive)
            throw await DeniedAsync(actor, "Admission", admission.Id, ct);
    }

    private async Task LockBedsAsync(Guid ward, Guid bed, BedAllocation? current, CancellationToken ct)
    {
        var wards = current is null ? new[] { ward } : new[] { ward, current.Bed.WardId };
        foreach (var id in wards.Distinct().Order()) await LockWardAsync(id, ct);
        var beds = current is null ? new[] { bed } : new[] { bed, current.BedId };
        foreach (var id in beds.Distinct().Order()) await LockBedAsync(id, ct);
    }

    private DateTime EndTime(BedAllocation current)
    {
        var now = Now;
        return now.Ticks - current.StartedAtUtc.Ticks < 10 ? current.StartedAtUtc.AddTicks(10) : now;
    }

    private void CloseAllocation(BedAllocation current, Guid actor, DateTime at)
    {
        (current.EndedAtUtc, current.EndedByUserId) = (at, actor);
        current.Bed.Condition = BedCondition.Cleaning;
        Touch(current.Bed, actor);
    }
}
