using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class BillingCatalogInput
{
    public Guid Token { get; set; }
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public BillingCategory Category { get; set; }
    [StringLength(40)] public string? SourceCode { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class BillingPriceInput
{
    public DateOnly From { get; set; }
    public DateOnly? Until { get; set; }
    public decimal Amount { get; set; }
}

public sealed class InvoiceInput
{
    public Guid Token { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ReplacesId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public List<InvoiceLineInput> Lines { get; set; } = [];
}

public sealed class InvoiceLineInput
{
    public Guid ServiceId { get; set; }
    public Guid? SourceId { get; set; }
    public decimal Quantity { get; set; } = 1;
}

public sealed class BillingTransitionInput
{
    public Guid Token { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class PaymentInput
{
    public Guid Token { get; set; }
    public Guid RequestKey { get; set; }
    public decimal Amount { get; set; }
    public BillingPaymentMethod Method { get; set; }
    [StringLength(80)] public string? BankReference { get; set; }
}

public sealed class BillingFilter
{
    [StringLength(100)] public string? Search { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public InvoiceStatus? Status { get; set; }
    public bool? Active { get; set; }
    public BillingCategory? Category { get; set; }
    public BillingPaymentMethod? Method { get; set; }
    public string? PaymentStatus { get; set; }
    public bool Oldest { get; set; }
    public bool Outstanding { get; set; }
    public bool Reversed { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record BillingPage<T>(BillingFilter Filter, IReadOnlyList<T> Rows, int Total);
public sealed record BillingPatient(Guid Id, string Number, string Name);
public sealed record BillingSource(Guid Id, BillingCategory Category, string Reference, string? Code, decimal Quantity, DateOnly ServiceDate);
public sealed record BillingEditor(Guid? Id, InvoiceInput Input, IReadOnlyList<BillingPatient> Patients, IReadOnlyList<BillableService> Services, IReadOnlyList<BillingSource> Sources);
public sealed record InvoicePage(PatientInvoice Invoice, decimal Paid, decimal Balance, Guid PaymentKey);
public sealed class InvoiceSummary
{
    public Guid Id { get; init; }
    public string? Number { get; init; }
    public string Patient { get; init; } = "";
    public string Reference { get; init; } = "";
    public InvoiceStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? IssuedAtUtc { get; init; }
    public decimal Total { get; init; }
    public decimal Paid { get; init; }
}
public sealed record BillingGroup(string Label, decimal Amount, int Count);
public sealed record BillingReports(BillingFilter Filter, IReadOnlyList<BillingGroup> Daily,
    IReadOnlyList<BillingGroup> Methods, IReadOnlyList<BillingGroup> Invoiced, BillingPage<PatientPayment> Payments);
public sealed record BillingDashboard(int Draft, int Issued, int Unpaid, int Partial, int Paid, int Reversed,
    decimal Outstanding, decimal TodayCollections, IReadOnlyList<InvoiceSummary> Recent, IReadOnlyList<PatientPayment> Payments);
