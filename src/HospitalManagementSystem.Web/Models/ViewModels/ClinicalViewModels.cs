using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class ClinicalEntryInput
{
    public Guid EncounterToken { get; set; }
    public Guid RecordToken { get; set; }
    public ClinicalRecordKind Kind { get; set; }
    public DateOnly RecordedDate { get; set; }
    [StringLength(4000)] public string? Content { get; set; }
    [StringLength(50)] public string? Code { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [StringLength(500)] public string? AmendmentReason { get; set; }
    public List<MedicineInput> Medicines { get; set; } = [];
}

public sealed class MedicineInput
{
    public Guid? Id { get; set; }
    [StringLength(150)] public string? Medicine { get; set; }
    [StringLength(60)] public string? Strength { get; set; }
    [StringLength(60)] public string? Dose { get; set; }
    [StringLength(40)] public string? DoseUnit { get; set; }
    [StringLength(80)] public string? Route { get; set; }
    [StringLength(120)] public string? Frequency { get; set; }
    [StringLength(80)] public string? Duration { get; set; }
    [Range(1, int.MaxValue)] public int? Quantity { get; set; }
    [StringLength(500)] public string? Instructions { get; set; }

    public bool HasContent => new[] { Medicine, Strength, Dose, DoseUnit, Route, Frequency, Duration, Instructions }
        .Any(value => !string.IsNullOrWhiteSpace(value)) || Quantity.HasValue;
}

public sealed class ClinicalTransitionInput
{
    public Guid EncounterToken { get; set; }
    public Guid RecordToken { get; set; }
    [StringLength(500)] public string? AmendmentReason { get; set; }
}

public sealed record ClinicalOverview(IReadOnlyList<Appointment> EligibleVisits, IReadOnlyList<ClinicalEncounter> Encounters);
public sealed record ClinicalEditor(Guid EncounterId, Guid? RecordId, bool IsAmendment, ClinicalEntryInput Input);
public sealed record ClinicalReport(string Title, IReadOnlyList<ClinicalEncounter> Encounters, DateTime GeneratedAtUtc, bool Printable,
    Guid? RecordId = null, DateOnly? From = null, DateOnly? To = null, Guid? EncounterId = null);
