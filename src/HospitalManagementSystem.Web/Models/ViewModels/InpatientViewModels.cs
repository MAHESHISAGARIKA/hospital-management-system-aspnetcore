using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class WardInput
{
    public Guid Token { get; set; }
    [Required, StringLength(30)] public string Code { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(120)] public string? Location { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class BedInput
{
    public Guid Token { get; set; }
    public Guid WardId { get; set; }
    [Required, StringLength(40)] public string Label { get; set; } = "";
    public BedCondition Condition { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class AdmissionInput
{
    public Guid RequestKey { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? EncounterId { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
}

public sealed class AdmissionAction
{
    public Guid Token { get; set; }
    public Guid WardId { get; set; }
    public Guid BedId { get; set; }
    public Guid NurseUserId { get; set; }
    public Guid AssignmentId { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class ObservationInput
{
    public Guid Token { get; set; }
    [Required] public ObservationKind? Kind { get; set; }
    [Required] public decimal? Value { get; set; }
    public Guid? CorrectsId { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class DischargeInput
{
    public Guid Token { get; set; }
    [Required] public DischargeDisposition? Disposition { get; set; }
    [Required, StringLength(500)] public string Diagnosis { get; set; } = "";
    [Required, StringLength(4000)] public string Summary { get; set; } = "";
}

public sealed class StayFilter
{
    [StringLength(100)] public string? Search { get; set; }
    public AdmissionStatus? Status { get; set; }
    public Guid? WardId { get; set; }
    public bool? Available { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public bool Oldest { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record StayPage<T>(StayFilter Filter, IReadOnlyList<T> Rows, int Total);
public sealed record WardCensus(Guid Id, string Code, string Name, bool Active, int Capacity, int Occupied, int Available);
public sealed record AdmissionPage(InpatientAdmission Admission, bool Doctor, bool Nurse, bool Reception, bool Print,
    IReadOnlyList<HospitalBed> Available, IReadOnlyList<ApplicationUser> Nurses)
{
    public IReadOnlyList<StayClinicalReference> Prescriptions { get; init; } = [];
    public IReadOnlyList<StayClinicalReference> LabReports { get; init; } = [];
}
public sealed record AdmissionEditor(AdmissionInput Input, IReadOnlyList<Patient> Patients, IReadOnlyList<Doctor> Doctors);
public sealed class StayAdmissionRow
{
    public Guid Id { get; init; }
    public string Number { get; init; } = "";
    public string PatientNumber { get; init; } = "";
    public string PatientName { get; init; } = "";
    public string DoctorName { get; init; } = "";
    public string? CurrentBed { get; init; }
    public AdmissionStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? AdmittedAtUtc { get; init; }
    public DateTime? DischargedAtUtc { get; init; }
}
public sealed record StayClinicalReference(Guid Id, string Label);
