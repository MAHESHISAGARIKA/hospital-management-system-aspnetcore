namespace HospitalManagementSystem.Web.Models.ViewModels;

public enum ReportKind { Patients, Appointments, Billing, Pharmacy, Laboratory, Staff, Inpatients, Outpatients }

public sealed class ReportFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? Department { get; set; }
    public Guid? Doctor { get; set; }
    public Guid? Item { get; set; }
    public Guid? Batch { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int? ExpiryDays { get; set; }
    public string? Order { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record ReportChoice(string Name, string Label, string Value, IReadOnlyList<(string Value, string Label)>? Options = null);
public sealed record ReportValue(string Label, decimal Value);
public sealed record ReportGroup(string Title, string Unit, IReadOnlyList<ReportValue> Values, bool Chart = false);
public sealed record ReportTable(string Title, string[] Headers, IReadOnlyList<string[]> Rows, bool HasMore, IReadOnlyList<Guid>? PatientIds = null);
public sealed record HospitalReport(ReportKind Kind, string DateMeaning, IReadOnlyList<ReportGroup> Groups,
    IReadOnlyList<ReportChoice> Choices, ReportTable? Table = null);
public sealed record ReportPage(ReportFilter Filter, DateTime GeneratedUtc, IReadOnlyList<HospitalReport> Reports,
    IReadOnlyList<ReportKind> Navigation, bool Dashboard);

