using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Inpatients;

public sealed partial class InpatientService
{
    public async Task<StayPage<StayAdmissionRow>> AdmissionsAsync(ClaimsPrincipal user, StayFilter filter, CancellationToken ct)
    {
        var (actor, doctor, nurse, reception) = await AccessAsync(user, ct);
        var query = db.InpatientAdmissions.AsNoTracking().AsQueryable();
        if (!reception)
            query = query.Where(x => x.Patient.IsActive && (doctor.HasValue && x.DoctorId == doctor ||
                nurse && x.Status == AdmissionStatus.Admitted && x.Assignments.Any(a => a.NurseUserId == actor && a.EndedAtUtc == null)));
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status);
        if (filter.WardId.HasValue) query = query.Where(x => x.Allocations.Any(a => a.EndedAtUtc == null && a.Bed.WardId == filter.WardId));
        if (Clean(filter.Search) is { } term)
        {
            var search = term.ToUpperInvariant();
            query = query.Where(x => x.Number.ToUpper().Contains(search) || x.Patient.PatientNumber.ToUpper() == search);
        }
        if (filter.From > filter.To || filter.From is { Year: < 2 or > 9998 } || filter.To is { Year: < 2 or > 9998 })
            throw new StayException("Choose a valid date range.");
        if (filter.From.HasValue)
        {
            var start = AppointmentService.ToUtc(filter.From.Value, TimeOnly.MinValue);
            query = filter.Status == AdmissionStatus.Discharged
                ? query.Where(x => x.Discharge != null && x.Discharge.SignedAtUtc >= start)
                : query.Where(x => (x.AdmittedAtUtc ?? x.CreatedAtUtc) >= start);
        }
        if (filter.To.HasValue)
        {
            var end = AppointmentService.ToUtc(filter.To.Value.AddDays(1), TimeOnly.MinValue);
            query = filter.Status == AdmissionStatus.Discharged
                ? query.Where(x => x.Discharge != null && x.Discharge.SignedAtUtc < end)
                : query.Where(x => (x.AdmittedAtUtc ?? x.CreatedAtUtc) < end);
        }
        var projected = query.Select(x => new StayAdmissionRow
        {
            Id = x.Id, Number = x.Number, PatientNumber = x.Patient.PatientNumber,
            PatientName = x.Patient.FirstName + " " + x.Patient.LastName, DoctorName = x.Doctor.FirstName + " " + x.Doctor.LastName,
            Status = x.Status, CreatedAtUtc = x.CreatedAtUtc, AdmittedAtUtc = x.AdmittedAtUtc,
            DischargedAtUtc = x.Discharge != null ? x.Discharge.SignedAtUtc : null,
            CurrentBed = x.Allocations.Where(a => a.EndedAtUtc == null).Select(a => a.Bed.Ward.Code + " / " + a.Bed.Label).FirstOrDefault()
        });
        var page = await PageAsync(filter.Oldest ? projected.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id) :
            projected.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id), filter, ct);
        await audit.WriteAsync("InpatientWorklistViewed", actor, null, "Admission worklist", ct);
        return page;
    }

    public async Task<StayAdmissionRow[]> ClinicalHistoryAsync(ClaimsPrincipal user, Guid patientId, Guid? encounterId, CancellationToken ct)
    {
        var (_, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        return await db.InpatientAdmissions.AsNoTracking()
            .Where(x => x.PatientId == patientId && x.DoctorId == doctor && x.Patient.IsActive &&
                (!encounterId.HasValue || x.OriginEncounterId == encounterId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new StayAdmissionRow
            {
                Id = x.Id, Number = x.Number, Status = x.Status, CreatedAtUtc = x.CreatedAtUtc,
                AdmittedAtUtc = x.AdmittedAtUtc, DischargedAtUtc = x.Discharge != null ? x.Discharge.SignedAtUtc : null
            }).ToArrayAsync(ct);
    }

    public async Task<AdmissionPage> DetailsAsync(ClaimsPrincipal user, Guid id, bool print, CancellationToken ct)
    {
        var (actor, doctor, nurse, reception) = await AccessAsync(user, ct);
        var admission = await LoadAsync(id, actor, ct, track: false);
        var doctorAccess = doctor == admission.DoctorId && admission.Patient.IsActive;
        var nurseAccess = nurse && NurseAssigned(admission, actor) && admission.Patient.IsActive;
        if (print ? !doctorAccess || admission.Discharge is null : !doctorAccess && !nurseAccess && !reception)
            throw await DeniedAsync(actor, "Admission", id, ct);
        var beds = reception && (admission.Status == AdmissionStatus.Approved || admission.Status == AdmissionStatus.Admitted)
            ? await Available(db.HospitalBeds.AsNoTracking()).Include(b => b.Ward)
                .OrderBy(b => b.Ward.Code).ThenBy(b => b.Label).ToArrayAsync(ct) : [];
        var nurses = doctorAccess && admission.Status == AdmissionStatus.Admitted
            ? (await users.GetUsersInRoleAsync(HospitalRoles.Nurse)).Where(u => u.IsEnabled).OrderBy(u => u.UserName).ToArray() : [];
        if (!doctorAccess && !nurseAccess)
        {
            admission.Reason = "";
            admission.CancellationReason = null;
            admission.Discharge = null;
            admission.Observations.Clear();
            admission.Assignments.Clear();
            admission.OriginEncounterId = null;
            foreach (var allocation in admission.Allocations) allocation.TransferReason = null;
        }
        else if (!doctorAccess)
        {
            admission.Reason = "";
            admission.OriginEncounterId = null;
            admission.Assignments.RemoveAll(a => a.NurseUserId != actor);
        }

        StayClinicalReference[] prescriptions = [];
        StayClinicalReference[] labs = [];
        if (doctorAccess && admission.OriginEncounterId is Guid encounterId &&
            await clinical.Permitted(doctor!.Value).AnyAsync(e => e.Id == encounterId, ct))
        {
            var signed = await db.ClinicalRecords.AsNoTracking()
                .Where(record => record.EncounterId == encounterId && record.Kind == ClinicalRecordKind.Prescription &&
                    record.Status == ClinicalStatus.Finalized && !db.ClinicalRecords.Any(next =>
                        next.PreviousRevisionId == record.Id && next.Status != ClinicalStatus.Draft))
                .Include(record => record.Items).OrderByDescending(record => record.FinalizedAtUtc).ToArrayAsync(ct);
            prescriptions = signed.Select(record => new StayClinicalReference(record.Id,
                string.Join(", ", record.Items.OrderBy(item => item.Position).Select(item => item.Medicine)))).ToArray();
            labs = await db.LaboratoryOrders.AsNoTracking().Where(order => order.EncounterId == encounterId && order.Status == LabOrderStatus.Completed)
                .OrderBy(order => order.Number).Select(order => new StayClinicalReference(order.Id, order.Number)).ToArrayAsync(ct);
        }
        else admission.OriginEncounterId = null;

        await audit.WriteAsync(print ? "InpatientDischargeSummaryViewed" : "InpatientAdmissionViewed",
            actor, null, $"Admission {id:D}", ct);
        return new(admission, doctorAccess, nurseAccess, reception, print, beds, nurses)
        { Prescriptions = prescriptions, LabReports = labs };
    }

    public async Task AssignAsync(ClaimsPrincipal user, Guid id, AdmissionAction input, bool end, CancellationToken ct)
    {
        var (actor, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        await CheckDoctorAsync(admission, actor, doctor, ct);
        CheckToken(input.Token, admission.Token);
        RequireActive(admission);
        if (end)
        {
            var current = admission.Assignments.SingleOrDefault(a => a.Id == input.AssignmentId && a.EndedAtUtc is null)
                ?? throw new StayException("This assignment is not active.");
            (current.EndedAtUtc, current.EndedByUserId) = (Now, actor);
        }
        else
        {
            var nurse = await users.FindByIdAsync(input.NurseUserId.ToString());
            if (nurse is not { IsEnabled: true } || !await users.IsInRoleAsync(nurse, HospitalRoles.Nurse) ||
                admission.Assignments.Any(a => a.NurseUserId == input.NurseUserId && a.EndedAtUtc is null))
                throw new StayException("Choose an enabled Nurse who is not already assigned.");
            db.InpatientCareAssignments.Add(new InpatientCareAssignment
            {
                Id = Guid.CreateVersion7(), AdmissionId = id, NurseUserId = nurse.Id, AssignedAtUtc = Now, AssignedByUserId = actor
            });
        }
        Touch(admission, actor);
        await CommitAsync(tx, end ? "InpatientCareAssignmentEnded" : "InpatientCareAssigned", actor, $"Admission {id:D}", ct);
    }

    public async Task ObserveAsync(ClaimsPrincipal user, Guid id, ObservationInput input, CancellationToken ct)
    {
        Validate(input);
        if (input.Kind is not { } kind || input.Value is not { } value || !Enum.IsDefined(kind) || decimal.Round(value, 2) != value ||
            value < 0 || value >= 100000000m || kind == ObservationKind.OxygenSaturationPercent && value > 100)
            throw new StayException("Enter a valid measurement in the specified unit with at most two decimals.");
        var (actor, doctor, nurse, _) = await AccessAsync(user, ct);
        await using var tx = await BeginAsync(ct);
        await LockAdmissionAsync(id, ct);
        var admission = await LoadAsync(id, actor, ct);
        if (!admission.Patient.IsActive || doctor != admission.DoctorId && !(nurse && NurseAssigned(admission, actor)))
            throw await DeniedAsync(actor, "Admission", id, ct);
        RequireActive(admission);
        CheckToken(input.Token, admission.Token);
        string? reason = null;
        if (input.CorrectsId.HasValue)
        {
            var previous = admission.Observations.SingleOrDefault(o => o.Id == input.CorrectsId);
            if (previous is null || previous.Kind != kind || doctor != admission.DoctorId && previous.RecordedByUserId != actor ||
                admission.Observations.Any(o => o.CorrectsId == input.CorrectsId))
                throw new StayException("Choose a current observation that you are authorized to correct.");
            reason = Reason(input.Reason);
        }
        var record = new InpatientObservation { Id = Guid.CreateVersion7(), AdmissionId = id, Kind = kind,
            Value = value, RecordedAtUtc = Now, RecordedByUserId = actor, CorrectsId = input.CorrectsId, CorrectionReason = reason };
        db.InpatientObservations.Add(record);
        Touch(admission, actor);
        await CommitAsync(tx, input.CorrectsId.HasValue ? "InpatientObservationCorrected" : "InpatientObservationRecorded",
            actor, $"Admission {id:D} Observation {record.Id:D}", ct);
    }

    public async Task<AppointmentSearchViewModel> OutpatientsAsync(ClaimsPrincipal user, AppointmentSearchViewModel filter,
        bool today, IAppointmentService appointments, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Doctor, HospitalRoles.Receptionist);
        Guid? doctorAccount = null;
        if (user.IsInRole(HospitalRoles.Doctor) && !user.IsInRole(HospitalRoles.Receptionist))
            doctorAccount = (await clinical.CurrentDoctorAsync(user, ct)).Actor;
        if (today) filter.Date ??= Today;
        if (filter.Date is { Year: < 2 or > 9998 })
            throw new StayException("Choose a valid visit date.");
        var result = await appointments.SearchAsync(filter, doctorAccount, ct);
        await audit.WriteAsync("OutpatientWorklistViewed", actor, null, "Appointment worklist", ct);
        return result;
    }

    public async Task<(Appointment Appointment, Guid? EncounterId, bool Clinical)> VisitAsync(
        ClaimsPrincipal user, Guid id, IAppointmentService appointments, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Doctor, HospitalRoles.Receptionist);
        Guid? doctor = user.IsInRole(HospitalRoles.Doctor) && !user.IsInRole(HospitalRoles.Receptionist)
            ? (await clinical.CurrentDoctorAsync(user, ct)).Doctor : null;
        var appointment = await appointments.GetAsync(id, ct);
        if (appointment is null || doctor.HasValue && (appointment.DoctorId != doctor || !appointment.Patient.IsActive))
            throw await DeniedAsync(actor, "Appointment", id, ct);
        var encounter = doctor.HasValue ? await clinical.Permitted(doctor.Value).Where(e => e.AppointmentId == id)
            .Select(e => (Guid?)e.Id).SingleOrDefaultAsync(ct) : null;
        await audit.WriteAsync("OutpatientVisitViewed", actor, null, $"Appointment {id:D}", ct);
        return (appointment, encounter, doctor.HasValue);
    }

    private async Task<(Guid Actor, Guid? Doctor, bool Nurse, bool Reception)> AccessAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Doctor, HospitalRoles.Nurse, HospitalRoles.Receptionist);
        Guid? doctor = user.IsInRole(HospitalRoles.Doctor) ? (await clinical.CurrentDoctorAsync(user, ct)).Doctor : null;
        return (actor, doctor, user.IsInRole(HospitalRoles.Nurse), user.IsInRole(HospitalRoles.Receptionist));
    }

    private static bool NurseAssigned(InpatientAdmission admission, Guid actor)
        => admission.Status == AdmissionStatus.Admitted && admission.Assignments.Any(a => a.NurseUserId == actor && a.EndedAtUtc == null);
    private static void RequireActive(InpatientAdmission admission)
    {
        if (admission.Status != AdmissionStatus.Admitted) throw new StayException("This admission is no longer active.");
    }
}
