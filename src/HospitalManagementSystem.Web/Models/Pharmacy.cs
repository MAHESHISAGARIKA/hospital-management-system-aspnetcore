namespace HospitalManagementSystem.Web.Models;

public enum PharmacyUnit { Tablet, Capsule, Millilitre, Gram, Each }
public enum BatchStatus { Available, Quarantined, Recalled }
public enum StockMovementKind { Received, Dispensed, Increase, Decrease, Quarantined, Recalled, Removed }

public sealed class Medicine
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string GenericName { get; set; } = "";
    public string? BrandName { get; set; }
    public string Strength { get; set; } = "";
    public string Form { get; set; } = "";
    public PharmacyUnit Unit { get; set; }
    public string? Manufacturer { get; set; }
    public string? Description { get; set; }
    public decimal ReorderThreshold { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public List<MedicineBatch> Batches { get; set; } = [];
}

public sealed class MedicineBatch
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public string Number { get; set; } = "";
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal Available { get; set; }
    public PharmacyUnit Unit { get; set; }
    public DateOnly Expiry { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public BatchStatus Status { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class PharmacyStockMovement
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public MedicineBatch Batch { get; set; } = null!;
    public PharmacyUnit Unit { get; set; }
    public decimal Quantity { get; set; }
    public StockMovementKind Kind { get; set; }
    public Guid ActorId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public Guid RequestKey { get; set; }
    public Guid? DispenseItemId { get; set; }
}

public sealed class PrescriptionDispense
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid PrescriptionId { get; set; }
    public ClinicalRecord Prescription { get; set; } = null!;
    public Guid ActorId { get; set; }
    public Guid RequestKey { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public bool IsPartial { get; set; }
    public List<PrescriptionDispenseItem> Lines { get; set; } = [];
}

public sealed class PrescriptionDispenseItem
{
    public Guid Id { get; set; }
    public Guid DispenseId { get; set; }
    public Guid PrescriptionId { get; set; }
    public Guid PrescriptionItemId { get; set; }
    public PrescriptionItem PrescriptionItem { get; set; } = null!;
    public Guid BatchId { get; set; }
    public MedicineBatch Batch { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string? FefoOverrideReason { get; set; }
}
