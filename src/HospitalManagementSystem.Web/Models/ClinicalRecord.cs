namespace HospitalManagementSystem.Web.Models;

public enum ClinicalRecordKind { Diagnosis, Prescription, Treatment }
public enum ClinicalStatus { Draft, Finalized, Voided }

public sealed class ClinicalEncounter
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public ICollection<ClinicalRecord> Records { get; set; } = [];
}

public sealed class ClinicalRecord
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public ClinicalRecordKind Kind { get; set; }
    public ClinicalStatus Status { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public ClinicalRecord? PreviousRevision { get; set; }
    public string? AmendmentReason { get; set; }
    public string? Content { get; set; }
    public string? Code { get; set; }
    public string? Notes { get; set; }
    public DateOnly RecordedDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public ICollection<PrescriptionItem> Items { get; set; } = [];
}

public sealed class PrescriptionItem
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }
    public int Position { get; set; }
    public string Medicine { get; set; } = "";
    public string? Strength { get; set; }
    public string Dose { get; set; } = "";
    public string DoseUnit { get; set; } = "";
    public string Route { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string Duration { get; set; } = "";
    public int? Quantity { get; set; }
    public string? Instructions { get; set; }
}
