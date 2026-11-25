using System.Globalization;
using System.Security.Claims;
using System.Text;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize, Route("Reports"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(HospitalReportingService reports, IAuditLogService audit) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View(new ReportPage(new(), reports.Now, [], Allowed(), false));

    [HttpGet("{kind}")]
    public Task<IActionResult> Show(ReportKind kind, ReportFilter filter, CancellationToken ct) =>
        Render(kind, filter, false, false, ct);

    [HttpGet("{kind}/Print")]
    public Task<IActionResult> Print(ReportKind kind, ReportFilter filter, CancellationToken ct) =>
        Render(kind, filter, false, true, ct);

    [HttpGet("{kind}/Csv")]
    public Task<IActionResult> Export(ReportKind kind, ReportFilter filter, CancellationToken ct) =>
        Render(kind, filter, true, false, ct);

    private ReportKind[] Allowed() => Enum.GetValues<ReportKind>().Where(k => ReportAccess.Allows(User, k)).ToArray();

    private async Task<IActionResult> Render(ReportKind kind, ReportFilter filter, bool csv, bool print, CancellationToken ct)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(kind)) return BadRequest("Invalid report filters.");
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (Guid?)null;
        if (!ReportAccess.Allows(User, kind))
        {
            await audit.WriteAsync("ReportAccessDenied", actor, null, kind.ToString(), ct);
            return Forbid();
        }
        if (!reports.Normalize(filter)) return BadRequest("Choose a valid date range of up to 366 days and valid filters.");
        HospitalReport report;
        try { report = await reports.Read(User, kind, filter, false, ct); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException error) { return BadRequest(error.Message); }

        if (csv)
        {
            await audit.WriteAsync("ReportExported", actor, null, kind.ToString(), ct);
            // Export aggregate sections only. Patient, staff, and batch detail tables stay in the browser.
            var output = new StringBuilder("Section,Label,Value,Unit\r\n");
            foreach (var group in report.Groups)
                foreach (var value in group.Values)
                    output.AppendJoin(",", Cell(group.Title), Cell(value.Label),
                        value.Value.ToString(CultureInfo.InvariantCulture), Cell(group.Unit)).Append("\r\n");
            return File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray(),
                "text/csv; charset=utf-8", $"{kind}.csv");
        }
        if (kind == ReportKind.Patients) await audit.WriteAsync("PatientReportViewed", actor, null, kind.ToString(), ct);
        ViewData["ReportPrint"] = print;
        return View("Show", new ReportPage(filter, reports.Now, [report], Allowed(), false));
    }

    private static string Cell(string value)
    {
        var trimmed = value.TrimStart();
        if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) ||
            (value.Length > 0 && "\t\r\n".Contains(value[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

