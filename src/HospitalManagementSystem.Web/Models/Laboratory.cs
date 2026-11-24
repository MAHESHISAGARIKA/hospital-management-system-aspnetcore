namespace HospitalManagementSystem.Web.Models;

public enum LabResultKind { Quantitative, Qualitative }
public enum LabSampleStatus { Collected, Received, Processing, Rejected }
public enum LabResultStatus { Draft, Submitted, Verified }
public enum LabOrderStatus { Requested, SampleCollected, InProgress, ResultsEntered, Completed, Cancelled }

public interface ILaboratoryTracked
{
    Guid Id { get; set; }
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
    Guid CreatedByUserId { get; set; }
    Guid UpdatedByUserId { get; set; }
    Guid ConcurrencyToken { get; set; }
}

public sealed class LaboratoryTest : ILaboratoryTracked
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Specimen { get; set; } = "";
    public string? Unit { get; set; }
    public LabResultKind Kind { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class LaboratoryOrder : ILaboratoryTracked
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid EncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid RequestKey { get; set; }
    public string? Instructions { get; set; }
    public LabOrderStatus Status { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public List<LaboratoryOrderItem> Items { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class LaboratoryOrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid TestId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Specimen { get; set; } = "";
    public string? Unit { get; set; }
    public LabResultKind Kind { get; set; }
    public List<LaboratorySample> Samples { get; set; } = [];
    public List<LaboratoryResult> Results { get; set; } = [];
}

public sealed class LaboratorySample : ILaboratoryTracked
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string Accession { get; set; } = "";
    public LabSampleStatus Status { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTime? ProcessingAtUtc { get; set; }
    public Guid? ProcessingByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class LaboratoryResult : ILaboratoryTracked
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid SampleId { get; set; }
    public LaboratorySample Sample { get; set; } = null!;
    public Guid? PreviousId { get; set; }
    public string? AmendmentReason { get; set; }
    public decimal? Value { get; set; }
    public string? QualitativeValue { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
    public LabResultStatus Status { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public string? VerifierName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
