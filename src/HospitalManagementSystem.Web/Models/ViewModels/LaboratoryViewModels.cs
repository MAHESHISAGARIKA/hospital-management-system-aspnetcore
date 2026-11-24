using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class LabCatalogInput
{
    public Guid Token { get; set; }
    [Required, StringLength(30)] public string Code { get; set; } = "";
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Specimen { get; set; } = "";
    [StringLength(40)] public string? Unit { get; set; }
    public LabResultKind Kind { get; set; }
    public bool Active { get; set; } = true;
}
public sealed class LabOrderInput
{
    public Guid RequestKey { get; set; }
    public List<Guid> TestIds { get; set; } = [];
    [StringLength(1000)] public string? Instructions { get; set; }
}
public sealed class LabActionInput
{
    public Guid Token { get; set; }
    public Guid ItemId { get; set; }
    public Guid SampleId { get; set; }
    public Guid ResultId { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    [StringLength(30)] public string? PatientNumber { get; set; }
    [StringLength(36)] public string? OrderNumber { get; set; }
}
public sealed class LabResultInput
{
    public Guid Token { get; set; }
    public Guid ItemId { get; set; }
    public Guid SampleId { get; set; }
    public Guid ResultId { get; set; }
    public decimal? Value { get; set; }
    [StringLength(250)] public string? QualitativeValue { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    [StringLength(500)] public string? AmendmentReason { get; set; }
}
public sealed class LabQueueFilter
{
    public string? Search { get; set; }
    public DateOnly? Date { get; set; }
    public LabOrderStatus? Status { get; set; }
    public Guid? DoctorId { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record LabQueue(LabQueueFilter Filter, IReadOnlyList<LaboratoryOrder> Orders, int Total,
    IReadOnlyDictionary<LabOrderStatus, int> Workload, IReadOnlyList<Doctor> Doctors);
public sealed record LabRequestPage(Guid EncounterId, string Patient, LabOrderInput Input, IReadOnlyList<LaboratoryTest> Tests);
public sealed record LabOrderPage(LaboratoryOrder Order, bool Staff, bool CanCancel, bool Print, DateTime GeneratedAtUtc);
