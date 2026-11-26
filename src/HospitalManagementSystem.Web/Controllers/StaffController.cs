using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize, AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Staff")]
public sealed class StaffController(StaffService staff, HospitalDbContext db) : Controller
{
    [HttpGet("")]
    [HttpGet("Employees")]
    [HttpGet("Attendance")]
    [HttpGet("Leave/Approvals")]
    [HttpGet("Reports")]
    [HttpGet("My")]
    [HttpGet("Employees/{id:guid}")]
    public Task<IActionResult> Index(Guid? id, string? search, Guid? department, bool? active, DateOnly? date,
        StaffLeaveStatus? status, int page, CancellationToken ct)
        => RenderIndex(id, search, department, active, date, status, page, ct);

    private async Task<IActionResult> RenderIndex(Guid? id, string? search, Guid? department, bool? active, DateOnly? date,
        StaffLeaveStatus? status, int page, CancellationToken ct, EmployeeInput? submitted = null)
    {
        try
        {
            if ((submitted is null && !ModelState.IsValid) || date is { Year: < 1900 or > 9998 }) return BadRequest("Invalid filter.");
            var isSelfPage = Request.Path.Value?.TrimEnd('/').EndsWith("/My", StringComparison.OrdinalIgnoreCase) == true;
            var admin = User.IsInRole("Administrator") && !isSelfPage;
            if (!admin && !isSelfPage) return Forbid();
            await staff.Actor(User, admin, ct);
            if (admin && Request.Path.Value?.TrimEnd('/').Equals("/Staff", StringComparison.OrdinalIgnoreCase) == true)
            {
                var today = staff.Today;
                var start = Services.Appointments.AppointmentService.ToUtc(today, TimeOnly.MinValue);
                var stop = Services.Appointments.AppointmentService.ToUtc(today.AddDays(1), TimeOnly.MinValue);
                ViewData["StaffCounts"] = new Dictionary<string, int>
                {
                    ["Active employees"] = await db.Set<Employee>().CountAsync(e => e.Active, ct),
                    ["Inactive employees"] = await db.Set<Employee>().CountAsync(e => !e.Active, ct),
                    ["Today's attendance records"] = await db.Set<StaffAttendance>().CountAsync(x => x.InUtc < stop && (x.OutUtc == null || x.OutUtc > start), ct),
                    ["Currently clocked in"] = await db.Set<StaffAttendance>().CountAsync(x => x.OutUtc == null, ct),
                    ["Pending leave"] = await db.Set<StaffLeaveRequest>().CountAsync(x => x.Status == StaffLeaveStatus.Pending, ct)
                };
                ViewData["StaffDepartmentCounts"] = await StaffService.CurrentDepartmentCounts(db).ToArrayAsync(ct);
            }
            var selected = admin ? id.HasValue ? await staff.EmployeeAsync(id.Value, ct) : null : await staff.Mine(User, ct);
            if (!admin && id.HasValue && selected?.Id != id) return Forbid();
            var employees = db.Set<Employee>().AsNoTracking().AsQueryable();
            if (selected != null) employees = employees.Where(e => e.Id == selected.Id);
            if (active.HasValue) employees = employees.Where(e => e.Active == active);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToUpperInvariant();
                employees = employees.Where(e => e.Number.ToUpper().Contains(term) || e.Name.ToUpper().Contains(term) || e.JobTitle.ToUpper().Contains(term));
            }
            if (department.HasValue) employees = employees.Where(e => db.Set<StaffDepartment>().Any(d =>
                d.EmployeeId == e.Id && d.DepartmentId == department && d.UntilUtc == null));
            page = Math.Clamp(page, 1, 10000);
            var rows = await employees.OrderBy(e => e.Name).ThenBy(e => e.Id).Skip((page - 1) * 25).Take(25).ToArrayAsync(ct);
            var ids = rows.Select(e => e.Id).ToArray();
            var attendance = db.Set<StaffAttendance>().AsNoTracking().Where(a => ids.Contains(a.EmployeeId));
            if (date.HasValue)
            {
                var start = Services.Appointments.AppointmentService.ToUtc(date.Value, TimeOnly.MinValue);
                var stop = Services.Appointments.AppointmentService.ToUtc(date.Value.AddDays(1), TimeOnly.MinValue);
                attendance = attendance.Where(a => a.InUtc < stop && (a.OutUtc == null || a.OutUtc > start));
            }
            var leave = db.Set<StaffLeaveRequest>().AsNoTracking().Include(l => l.Type).Where(l => ids.Contains(l.EmployeeId));
            if (status.HasValue) leave = leave.Where(l => l.Status == status);
            if (date.HasValue) leave = leave.Where(l => l.From <= date && l.Through >= date);
            var history = await db.Set<StaffDepartment>().AsNoTracking().Include(d => d.Department)
                .Where(d => ids.Contains(d.EmployeeId) && (selected != null || d.UntilUtc == null)).OrderByDescending(d => d.FromUtc).ToArrayAsync(ct);
            ViewData["DepartmentChoices"] = await db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToArrayAsync(ct);
            if (admin)
                ViewData["Accounts"] = await db.Users.AsNoTracking().Where(u => u.IsEnabled).OrderBy(u => u.UserName).ToArrayAsync(ct);
            var requests = await leave.OrderByDescending(l => l.CreatedAtUtc).Take(100).ToArrayAsync(ct);
            if (admin)
            {
                var warnings = new Dictionary<Guid, string>();
                foreach (var request in requests.Where(l => l.Status == StaffLeaveStatus.Pending))
                {
                    var warning = await staff.Conflict(rows.First(e => e.Id == request.EmployeeId), request.From, request.Through, ct);
                    if (warning != null) warnings[request.Id] = warning;
                }
                ViewData["Warnings"] = warnings;
            }
            var sessions = await attendance.OrderByDescending(a => a.InUtc).Take(100).ToArrayAsync(ct);
            var sessionIds = sessions.Select(a => a.Id).ToArray();
            var requestIds = requests.Select(a => a.Id).ToArray();
            ViewData["Corrections"] = await db.Set<StaffAttendanceCorrection>().AsNoTracking()
                .Where(c => sessionIds.Contains(c.AttendanceId)).OrderByDescending(c => c.AtUtc).ToArrayAsync(ct);
            ViewData["Decisions"] = await db.Set<StaffLeaveDecision>().AsNoTracking().Where(d => requestIds.Contains(d.RequestId)).ToArrayAsync(ct);
            ViewData["SubmittedEmployee"] = submitted;
            return View(nameof(Index), new StaffPage(rows, history, sessions, requests,
                await db.Set<StaffLeaveType>().AsNoTracking().Where(t => admin || t.Active).OrderBy(t => t.Code).ToArrayAsync(ct),
                admin, selected, page));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (StaffFault error) { return BadRequest(error.Message); }
    }

    [Authorize(Roles = "Administrator"), HttpPost("Employees/Save/{id:guid?}")]
    public async Task<IActionResult> Save([FromRoute] Guid? id, [FromForm] EmployeeInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                await staff.SaveEmployee(User, id, input.Token.GetValueOrDefault(), input.Name!, input.Email!, input.Job!,
                    input.From!.Value, input.Active!.Value, input.Account, ct);
                TempData["StaffMessage"] = "Change saved.";
                return RedirectToAction(nameof(Index), new { id });
            }
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (StaffFault error) { ModelState.AddModelError(error.Field ?? string.Empty, error.Message); }

        return await RenderIndex(id, null, null, null, null, null, 1, ct, input);
    }

    [Authorize(Roles = "Administrator"), HttpPost("Departments/Transfer/{id:guid}")]
    public Task<IActionResult> Transfer(Guid id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Transfer(User, id, input.Token, input.Department, input.Reason, ct), id);

    [HttpPost("Attendance/ClockIn")]
    public Task<IActionResult> ClockIn(CancellationToken ct) => Run(() => staff.Clock(User, true, ct), self: true);
    [HttpPost("Attendance/ClockOut")]
    public Task<IActionResult> ClockOut(CancellationToken ct) => Run(() => staff.Clock(User, false, ct), self: true);

    [Authorize(Roles = "Administrator"), HttpPost("Attendance/Correct/{id:guid}")]
    public Task<IActionResult> Correct(Guid id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Correct(User, id, input.Token, input.Start, input.End, input.Reason, ct));

    [Authorize(Roles = "Administrator"), HttpPost("Leave/Types/{id:guid?}")]
    public Task<IActionResult> SaveType(Guid? id, StaffInput input, CancellationToken ct)
        => Run(() => staff.SaveType(User, id, input.Token, input.Code, input.Name, input.Active, ct));

    [HttpPost("Leave/Request/{id:guid?}")]
    public Task<IActionResult> RequestLeave(Guid? id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Request(User, id, input.Token, input.Type, input.From, input.Through, input.Reason, ct), self: true);

    [HttpPost("Leave/Withdraw/{id:guid}")]
    public Task<IActionResult> Withdraw(Guid id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Decide(User, id, input.Token, StaffLeaveStatus.Withdrawn, null, ct), self: true);
    [Authorize(Roles = "Administrator"), HttpPost("Leave/Approve/{id:guid}")]
    public Task<IActionResult> Approve(Guid id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Decide(User, id, input.Token, StaffLeaveStatus.Approved, null, ct));
    [Authorize(Roles = "Administrator"), HttpPost("Leave/Reject/{id:guid}")]
    public Task<IActionResult> Reject(Guid id, StaffInput input, CancellationToken ct)
        => Run(() => staff.Decide(User, id, input.Token, StaffLeaveStatus.Rejected, input.Reason, ct));

    private async Task<IActionResult> Run(Func<Task> action, Guid? id = null, bool self = false)
    {
        try
        {
            if (!ModelState.IsValid) throw new StaffFault("Check the required fields and formats.");
            await action();
            TempData["StaffMessage"] = "Change saved.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (StaffFault error) { TempData["StaffMessage"] = error.Message; }
        return self ? Redirect("~/Staff/My") : RedirectToAction(nameof(Index), new { id });
    }
}
