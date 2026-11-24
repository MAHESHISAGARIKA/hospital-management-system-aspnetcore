using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class MedicineCatalogInput
{
    public Guid Token { get; set; }
    [Required, StringLength(30)] public string Code { get; set; } = "";
    [Required, StringLength(150)] public string GenericName { get; set; } = "";
    [StringLength(150)] public string? BrandName { get; set; }
    [Required, StringLength(80)] public string Strength { get; set; } = "";
    [Required, StringLength(80)] public string Form { get; set; } = "";
    [StringLength(150)] public string? Manufacturer { get; set; }
    [StringLength(500)] public string? Description { get; set; }
    public PharmacyUnit Unit { get; set; }
    public decimal ReorderThreshold { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class StockReceiptInput
{
    public Guid MedicineId { get; set; }
    public Guid RequestKey { get; set; }
    [Required, StringLength(60)] public string Number { get; set; } = "";
    [StringLength(150)] public string? Supplier { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public DateOnly Expiry { get; set; }
    public decimal Quantity { get; set; }
}
public sealed class StockChangeInput
{
    public Guid Token { get; set; }
    public Guid RequestKey { get; set; }
    public StockMovementKind Kind { get; set; }
    public decimal Quantity { get; set; }
    [Required, StringLength(500)] public string Reason { get; set; } = "";
}
public sealed class DispenseInput
{
    public Guid RequestKey { get; set; }
    [StringLength(30)] public string? PatientNumber { get; set; }
    public List<DispenseLineInput> Lines { get; set; } = [];
}
public sealed class DispenseLineInput
{
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    [StringLength(500)] public string? FefoOverrideReason { get; set; }
}
public sealed class PharmacyFilter
{
    [StringLength(100)] public string? Search { get; set; }
    public Guid? MedicineId { get; set; }
    public Guid? BatchId { get; set; }
    public bool? Active { get; set; }
    public bool LowStock { get; set; }
    public BatchStatus? Status { get; set; }
    public StockMovementKind? Kind { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? Days { get; set; }
    public bool Expired { get; set; }
    public string? DispensingStatus { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record PharmacyPage<T>(PharmacyFilter Filter, IReadOnlyList<T> Rows, int Total);
public sealed record MedicineStock
{
    public Medicine Medicine { get; init; } = null!;
    public decimal Eligible { get; init; }
}
public sealed record PrescriptionRow
{
    public Guid Id { get; init; }
    public string PatientNumber { get; init; } = "";
    public string PatientName { get; init; } = "";
    public string Doctor { get; init; } = "";
    public DateTime SignedUtc { get; init; }
    public string Status { get; init; } = "";
}
public sealed record DispenseChoice(PrescriptionItem Item, decimal Remaining, IReadOnlyList<MedicineBatch> Batches);
public sealed record DispensingPage(PrescriptionRow Prescription, IReadOnlyList<DispenseChoice> Items, IReadOnlyList<PrescriptionDispense> History, bool CanDispense, Guid RequestKey);
public sealed record PharmacyDashboard(int Active, int Low, int Expired, int Expiring, int Pending, int Partial, int Completed,
    IReadOnlyList<PharmacyStockMovement> Recent, IReadOnlyList<MedicineStock> Stock, int WarningDays);
public sealed record ConsumptionRow(string Code, string Name, PharmacyUnit Unit, decimal Quantity);
