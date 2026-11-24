namespace HospitalManagementSystem.Web.Models;

public enum BillingCategory { Consultation, Laboratory, Pharmacy, Admission, Other }
public enum InvoiceStatus { Draft, Issued, Voided }
public enum BillingPaymentMethod { Cash, BankTransfer }

public sealed class BillableService
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public BillingCategory Category { get; set; }
    public string? SourceCode { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid Token { get; set; }
    public List<BillingPrice> Prices { get; set; } = [];
}

public sealed class BillingPrice
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly? Until { get; set; }
    public decimal Amount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PatientInvoice
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string? Number { get; set; }
    public Guid? ReplacesId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public decimal Total { get; set; }
    public string? PatientLabel { get; set; }
    public string? PatientReference { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid Token { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
    public List<PatientInvoiceItem> Items { get; set; } = [];
    public List<PatientPayment> Payments { get; set; } = [];
}

public sealed class PatientInvoiceItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid PriceId { get; set; }
    public BillingPrice Price { get; set; } = null!;
    public BillingCategory Category { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? AppointmentId { get; set; }
    public Guid? LabItemId { get; set; }
    public Guid? DispenseItemId { get; set; }
    public bool Claimed { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public sealed class PatientPayment
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public PatientInvoice Invoice { get; set; } = null!;
    public Guid RequestKey { get; set; }
    public string ReceiptNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public BillingPaymentMethod Method { get; set; }
    public string? BankReference { get; set; }
    public Guid ActorId { get; set; }
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public PaymentReversal? Reversal { get; set; }
}

public sealed class PaymentReversal
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid ActorId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Reason { get; set; } = "";
}
