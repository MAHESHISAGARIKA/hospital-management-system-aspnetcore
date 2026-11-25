using System.Globalization;
using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Reporting;

public sealed partial class HospitalReportingService
{
    private async Task<HospitalReport> StaffReport(ReportFilter f, DateTime from, DateTime to, bool dashboard, CancellationToken ct)
    {
        var assignments = db.Set<StaffDepartment>().AsNoTracking();
        var current = assignments.Where(a => a.UntilUtc == null);
        var employees = db.Set<Employee>().AsNoTracking().Where(e => !f.Item.HasValue || e.Id == f.Item);
        if (f.Department.HasValue)
        {
            current = current.Where(a => a.DepartmentId == f.Department);
            employees = employees.Where(e => current.Any(a => a.EmployeeId == e.Id));
        }
        var sessions = db.Set<StaffAttendance>().AsNoTracking().Where(s => employees.Any(e => e.Id == s.EmployeeId));
        var leave = db.Set<StaffLeaveRequest>().AsNoTracking().Where(r => employees.Any(e => e.Id == r.EmployeeId) &&
            (!f.Batch.HasValue || r.TypeId == f.Batch));
        var active = await employees.CountAsync(e => e.Active, ct);
        var inactive = await employees.CountAsync(e => !e.Active, ct);
        var groups = new List<ReportGroup> { Group("Current staff",
            Value("Total employees", active + inactive), Value("Active employees", active), Value("Inactive employees", inactive),
            Value("Currently clocked in", await sessions.Where(s => s.OutUtc == null).Select(s => s.EmployeeId).Distinct().CountAsync(ct)),
            Value("Pending staff leave approvals", await leave.CountAsync(r => r.Status == StaffLeaveStatus.Pending, ct))) };
        if (dashboard) return new(ReportKind.Staff, "Current staff and pending leave", groups, []);
        var byDepartment = await current.Where(a => employees.Any(e => e.Id == a.EmployeeId))
            .GroupBy(a => a.Department.Name).Select(g => new { g.Key, Count = g.Select(a => a.EmployeeId).Distinct().Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key).Take(100).ToArrayAsync(ct);
        groups.Add(Series("Employees by current department (up to 100)", byDepartment.Select(g => Value(g.Key, g.Count))));
        var titles = await employees.GroupBy(e => e.JobTitle).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key).Take(100).ToArrayAsync(ct);
        groups.Add(Series("Employees by job title (up to 100)", titles.Select(g => Value(g.Key, g.Count))));
        var attendance = sessions.Where(s => s.InUtc < to && (s.OutUtc == null || s.OutUtc > from));
        var seconds = await attendance.Where(s => s.OutUtc != null).SumAsync(s =>
            ((s.OutUtc!.Value < to ? s.OutUtc.Value : to) - (s.InUtc > from ? s.InUtc : from)).TotalSeconds, ct);
        groups.Add(Group("Attendance during period", Value("Attendance sessions overlapping period", await attendance.CountAsync(ct)),
            Value("Worked hours from completed sessions", Math.Round((decimal)(seconds / 3600), 2))));
        groups.Add(await Trend("Clock-ins by local day", sessions.Where(s => s.InUtc >= from && s.InUtc < to).Select(s => s.InUtc), false, ct));

        leave = leave.Where(r => r.From <= f.To && r.Through >= f.From);
        if (Status<StaffLeaveStatus>(f) is { } status) leave = leave.Where(r => r.Status == status);
        var statuses = await leave.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToArrayAsync(ct);
        groups.Add(Series("Leave requests overlapping period", statuses.Select(s => Value(s.Key.ToString(), s.Count))));

        var history = assignments.Where(a => a.FromUtc < to && (a.UntilUtc == null || a.UntilUtc > from) &&
            (!f.Item.HasValue || a.EmployeeId == f.Item) && (!f.Department.HasValue || a.DepartmentId == f.Department));
        var rows = await Page(history.Join(db.Set<Employee>().AsNoTracking(), a => a.EmployeeId, e => e.Id, (a, e) => new { a, e })
            .OrderByDescending(x => x.a.FromUtc).ThenBy(x => x.a.Id), f)
            .Select(x => new { x.e.Number, x.e.Name, Department = x.a.Department.Name, x.a.FromUtc, x.a.UntilUtc }).ToArrayAsync(ct);
        var employeeChoices = await db.Set<Employee>().AsNoTracking().OrderBy(e => e.Name).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Name, e.Number }).Take(250).ToArrayAsync(ct);
        var leaveTypes = await db.Set<StaffLeaveType>().AsNoTracking().OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Name }).Take(250).ToArrayAsync(ct);
        return new(ReportKind.Staff, "Attendance uses overlap with the UTC-converted period. Leave uses inclusive local dates. Current staff counts use current assignments.", groups,
            [await Departments(f, ct), new("item", "Employee", f.Item?.ToString() ?? "", employeeChoices.Select(e => (e.Id.ToString(), e.Name + " (" + e.Number + ")")).ToArray()),
            new("batch", "Leave type", f.Batch?.ToString() ?? "", leaveTypes.Select(t => (t.Id.ToString(), t.Name)).ToArray()),
            EnumChoice<StaffLeaveStatus>("Leave status", f.Status)],
            Table("Department assignment history", ["Employee number", "Employee", "Department", "From (Colombo)", "Until (Colombo)"],
                rows.Select(x => new[] { x.Number, x.Name, x.Department, DateLabel(x.FromUtc), x.UntilUtc.HasValue ? DateLabel(x.UntilUtc.Value) : "Current" }).ToArray()));
    }

    private async Task<HospitalReport> InpatientReport(DateTime from, DateTime to, ClaimsPrincipal user, Guid actor, bool dashboard, CancellationToken ct)
    {
        var hospital = ReportAccess.Registration(user);
        var doctor = user.IsInRole(HospitalRoles.Doctor);
        var nurse = user.IsInRole(HospitalRoles.Nurse);
        var admissions = db.InpatientAdmissions.AsNoTracking().Where(a => hospital ||
            (doctor && a.Doctor.ApplicationUserId == actor && a.Doctor.IsActive) ||
            (nurse && a.Assignments.Any(x => x.NurseUserId == actor && x.EndedAtUtc == null)));
        var admitted = admissions.Where(a => a.AdmittedAtUtc >= from && a.AdmittedAtUtc < to);
        var discharged = admissions.Where(a => a.Status == AdmissionStatus.Discharged && a.Discharge != null &&
            a.Discharge.SignedAtUtc >= from && a.Discharge.SignedAtUtc < to);
        var groups = new List<ReportGroup> { Group(hospital ? "Inpatient census" : "Assigned inpatient work",
            Value("Current inpatients", await admissions.CountAsync(a => a.Status == AdmissionStatus.Admitted, ct)),
            Value("Admissions in period", await admitted.CountAsync(ct)), Value("Discharges in period", await discharged.CountAsync(ct))) };
        if (hospital)
        {
            var bedRows = db.HospitalBeds.AsNoTracking().Select(b => new { b.Ward.Name, b.Condition, b.Active,
                WardActive = b.Ward.Active, Occupied = b.Allocations.Any(a => a.EndedAtUtc == null) });
            var beds = await bedRows.GroupBy(_ => 1).Select(g => new { Occupied = g.Count(b => b.Occupied),
                Available = g.Count(b => b.Active && b.WardActive && !b.Occupied && b.Condition == BedCondition.Ready),
                Maintenance = g.Count(b => b.Condition == BedCondition.Maintenance) }).SingleOrDefaultAsync(ct);
            groups.Add(Group("Current beds", Value("Occupied beds", beds?.Occupied ?? 0),
                Value("Available beds", beds?.Available ?? 0), Value("Beds under maintenance", beds?.Maintenance ?? 0)));
            var wards = await bedRows.Where(b => b.Occupied).GroupBy(b => b.Name).Select(g => new { g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).ThenBy(g => g.Key).Take(100).ToArrayAsync(ct);
            groups.Add(Series("Occupied beds by ward (up to 100)", wards.Select(w => Value(w.Key, w.Count))));
        }
        if (!dashboard)
        {
            groups.Add(await Trend("Admissions by day", admitted.Select(a => a.AdmittedAtUtc!.Value), false, ct));
            groups.Add(await Trend("Discharges by day", discharged.Select(a => a.Discharge!.SignedAtUtc), false, ct));
        }
        return new(ReportKind.Inpatients, "Admissions use first admission time. Discharges use signed time. Bed occupancy is current.", groups, []);
    }
}

