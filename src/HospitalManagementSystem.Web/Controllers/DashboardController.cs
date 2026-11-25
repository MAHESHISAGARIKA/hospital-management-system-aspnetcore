using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(UserManager<ApplicationUser> userManager, HospitalReportingService reports) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsEnabled) return Challenge();
        var filter = new ReportFilter { From = from, To = to };
        if (!ModelState.IsValid || !reports.Normalize(filter)) return BadRequest("Choose a valid reporting period of up to 366 days.");

        var navigation = Enum.GetValues<ReportKind>().Where(k => ReportAccess.Allows(User, k)).ToArray();
        var sections = new List<HospitalReport>();
        foreach (var report in navigation.Where(k => k != ReportKind.Outpatients))
            sections.Add(await reports.Read(User, report, filter, true, ct));

        ViewData["AccountEmail"] = user.Email;
        ViewData["AccountRoles"] = string.Join(", ", await userManager.GetRolesAsync(user));
        return View(new ReportPage(filter, reports.Now, sections, navigation, true));
    }
}
