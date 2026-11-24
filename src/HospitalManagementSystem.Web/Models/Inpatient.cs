namespace HospitalManagementSystem.Web.Models;

public enum AdmissionStatus { Pending, Approved, Admitted, Discharged, Cancelled }
public enum BedCondition { Ready, Cleaning, Maintenance, Unavailable }
public enum ObservationKind { TemperatureCelsius, PulsePerMinute, RespirationsPerMinute, OxygenSaturationPercent }
public enum DischargeDisposition { Home, Referred, Transferred, Other }

public sealed class HospitalWard
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Location { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool Active { get; set; }
    public Guid Token { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public List<HospitalBed> Beds { get; set; } = [];
}

public sealed class HospitalBed
{
    public Guid Id { get; set; }
    public Guid WardId { get; set; }
    public HospitalWard Ward { get; set; } = null!;
    public string Label { get; set; } = "";
    public BedCondition Condition { get; set; }
    public bool Active { get; set; }
    public Guid Token { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public List<BedAllocation> Allocations { get; set; } = [];
}

public sealed class InpatientAdmission
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid RequestKey { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid? OriginEncounterId { get; set; }
    public ClinicalEncounter? OriginEncounter { get; set; }
    public string Reason { get; set; } = "";
    public AdmissionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid Token { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? AdmittedAtUtc { get; set; }
    public Guid? AdmittedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public List<BedAllocation> Allocations { get; set; } = [];
    public List<InpatientCareAssignment> Assignments { get; set; } = [];
    public List<InpatientObservation> Observations { get; set; } = [];
    public InpatientDischarge? Discharge { get; set; }
}

public sealed class BedAllocation
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public Guid BedId { get; set; }
    public HospitalBed Bed { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public Guid StartedByUserId { get; set; }
    public Guid? EndedByUserId { get; set; }
    public string? TransferReason { get; set; }
}

public sealed class InpatientCareAssignment
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public Guid NurseUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public Guid? EndedByUserId { get; set; }
}

public sealed class InpatientObservation
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public ObservationKind Kind { get; set; }
    public decimal Value { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public Guid RecordedByUserId { get; set; }
    public Guid? CorrectsId { get; set; }
    public string? CorrectionReason { get; set; }
}

public sealed class InpatientDischarge
{
    public Guid Id { get; set; }
    public Guid AdmissionId { get; set; }
    public DischargeDisposition Disposition { get; set; }
    public string Diagnosis { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime SignedAtUtc { get; set; }
    public Guid SignedByUserId { get; set; }
}
