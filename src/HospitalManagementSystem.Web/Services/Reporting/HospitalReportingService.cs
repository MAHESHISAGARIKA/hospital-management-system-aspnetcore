using System.Globalization;
using System.Security.Claims;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Reporting;

public sealed partial class HospitalReportingService(HospitalDbContext db, TimeProvider clock)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(AppointmentService.ToColombo(Now));
    public const int PageSize = 25;

    public bool Normalize(ReportFilter filter)
    {
        filter.To ??= Today;
        if (filter.To.Value.Year is < 1900 or > 9998) return false;
        filter.From ??= filter.To.Value.AddDays(-29);
        return filter.From.Value.Year >= 1900 && filter.To.Value.Year <= 9998 &&
            filter.To.Value.DayNumber - filter.From.Value.DayNumber is >= 0 and < 366 &&
            filter.Page is >= 1 and <= 10000 && filter.ExpiryDays is null or (>= 0 and <= 365) &&
            (filter.Search?.Length ?? 0) <= 100 && filter.Order is null or "newest" or "oldest";
    }

    public async Task<HospitalReport> Read(ClaimsPrincipal user, ReportKind kind, ReportFilter filter, bool dashboard, CancellationToken ct)
    {
        if (!ReportAccess.Allows(user, kind) || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            !await db.Users.AnyAsync(x => x.Id == actor && x.IsEnabled, ct))
            throw new UnauthorizedAccessException();

        var from = Boundary(filter.From!.Value);
        var to = Boundary(filter.To!.Value.AddDays(1));
        var hospitalScope = ReportAccess.Registration(user);
        return kind switch
        {
            ReportKind.Patients => await PatientReport(filter, from, to, dashboard, ct),
            ReportKind.Appointments or ReportKind.Outpatients => await AppointmentReport(kind, filter, from, to, hospitalScope ? null : actor, dashboard, ct),
            ReportKind.Billing => await BillingReport(filter, from, to, dashboard, ct),
            ReportKind.Pharmacy => await PharmacyReport(filter, from, to, user.IsInRole(Authorization.HospitalRoles.Pharmacist) && !dashboard, dashboard, ct),
            ReportKind.Laboratory => await LaboratoryReport(filter, from, to, dashboard, ct),
            ReportKind.Staff => await StaffReport(filter, from, to, dashboard, ct),
            ReportKind.Inpatients => await InpatientReport(from, to, user, actor, dashboard, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static DateTime Boundary(DateOnly day) => AppointmentService.ToUtc(day, TimeOnly.MinValue);
    private static DateTime Local(DateTime utc) => AppointmentService.ToColombo(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    private static string DateLabel(DateTime utc) => Local(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static ReportValue Value(string label, decimal value) => new(label, value);
    private static ReportGroup Group(string title, params ReportValue[] values) => new(title, "", values);
    private static ReportGroup Series(string title, IEnumerable<ReportValue> values, string unit = "") =>
        new(title, unit, values.ToArray(), true);
    private static ReportGroup Money(string title, params ReportValue[] values) => new(title, "LKR", values);
    private static ReportChoice EnumChoice<T>(string label, string? selected) where T : struct, Enum =>
        new("status", label, selected ?? "", Enum.GetNames<T>().Select(x => (x, x)).ToArray());
    private static T? Status<T>(ReportFilter filter) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(filter.Status)) return null;
        if (Enum.TryParse<T>(filter.Status, true, out var value) && Enum.IsDefined(value)) return value;
        throw new ArgumentException("Choose a valid status.");
    }

    private async Task<ReportChoice> Departments(ReportFilter filter, CancellationToken ct) =>
        new("department", "Department", filter.Department?.ToString() ?? "",
            (await db.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync(ct))
            .Select(d => (d.Id.ToString(), d.Name)).ToArray());

    private async Task<ReportChoice> Doctors(ReportFilter filter, Guid? actor, CancellationToken ct) =>
        new("doctor", "Doctor", filter.Doctor?.ToString() ?? "",
            (await db.Doctors.AsNoTracking().Where(d => actor == null || d.ApplicationUserId == actor)
                .OrderBy(d => d.LastName).ThenBy(d => d.Id).Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName }).ToListAsync(ct))
            .Select(d => (d.Id.ToString(), d.Name)).ToArray());

    private static ReportTable Table(string title, string[] headings, string[][] rows) =>
        new(title, headings, rows.Take(PageSize).ToArray(), rows.Length > PageSize);

    private static ReportChoice DateOrder(ReportFilter filter) =>
        new("order", "Date order", filter.Order ?? "newest", [("newest", "Newest first"), ("oldest", "Oldest first")]);

    private static IQueryable<T> Page<T>(IQueryable<T> query, ReportFilter filter) =>
        query.Skip((filter.Page - 1) * PageSize).Take(PageSize + 1);

    private static IQueryable<DateTime> Colombo(IQueryable<DateTime> utc) =>
        utc.Select(x => x.AddMinutes(330));

    private static async Task<ReportGroup> Trend(string title, IQueryable<DateTime> utc, bool monthly, CancellationToken ct)
    {
        var days = await Colombo(utc).GroupBy(x => new { x.Year, x.Month, Day = monthly ? 1 : x.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .OrderBy(g => g.Year).ThenBy(g => g.Month).ThenBy(g => g.Day).ToListAsync(ct);
        return Series(title, days.Select(d => Value(new DateOnly(d.Year, d.Month, d.Day)
            .ToString(monthly ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture), d.Count)));
    }

    private async Task<HospitalReport> PatientReport(ReportFilter f, DateTime from, DateTime to, bool dashboard, CancellationToken ct)
    {
        var patients = db.Patients.AsNoTracking();
        var visits = db.Appointments.AsNoTracking().Where(x => x.StartAtUtc >= from && x.StartAtUtc < to);
        if (f.Department is Guid department)
        {
            visits = visits.Where(x => x.DepartmentId == department);
            patients = patients.Where(x => visits.Any(a => a.PatientId == x.Id));
        }
        if (f.Status is "Active") patients = patients.Where(p => p.IsActive);
        else if (f.Status is "Archived") patients = patients.Where(p => !p.IsActive);
        else if (!string.IsNullOrWhiteSpace(f.Status)) throw new ArgumentException("Choose a valid patient status.");
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var search = f.Search.Trim().ToUpperInvariant();
            patients = patients.Where(p => p.PatientNumber.ToUpper().Contains(search) ||
                (p.FirstName + " " + p.LastName).ToUpper().Contains(search));
        }
        var registered = patients.Where(p => p.CreatedAtUtc >= from && p.CreatedAtUtc < to);
        var active = await patients.CountAsync(p => p.IsActive, ct);
        var archived = await patients.CountAsync(p => !p.IsActive, ct);
        var groups = new List<ReportGroup> { Group("Patient registration",
            Value("Registered patients", active + archived), Value("Active patients", active), Value("Archived patients", archived),
            Value("New registrations", await registered.CountAsync(ct)),
            Value("Unique patients with completed appointments", await patients.CountAsync(p =>
                visits.Any(a => a.PatientId == p.Id && a.Status == AppointmentStatus.Completed), ct))) };
        groups.Add(await Trend("Registrations by month", registered.Select(p => p.CreatedAtUtc), true, ct));
        if (dashboard)
        {
            groups.Insert(0, Group("Current providers", Value("Active doctors", await db.Doctors.CountAsync(d => d.IsActive, ct))));
            return new(ReportKind.Patients, "Registration date", groups, []);
        }
        groups.Add(await Trend("Registrations by day", registered.Select(p => p.CreatedAtUtc), false, ct));
        var ordered = f.Order == "oldest" ? registered.OrderBy(p => p.CreatedAtUtc) : registered.OrderByDescending(p => p.CreatedAtUtc);
        var rows = await Page(ordered.ThenBy(p => p.Id), f)
            .Select(p => new { p.Id, p.PatientNumber, p.FirstName, p.LastName, p.IsActive, p.CreatedAtUtc }).ToArrayAsync(ct);
        return new(ReportKind.Patients, "Registration date for registrations. Appointment occurrence for activity and department matching.", groups,
            [new("status", "Patient status", f.Status ?? "", [("Active", "Active"), ("Archived", "Archived")]),
            new("search", "Patient name or number", f.Search ?? ""), await Departments(f, ct), DateOrder(f)],
            Table("Registrations in the selected period", ["Patient number", "Name", "Status", "Registered (Colombo)"],
                rows.Select(p => new[] { p.PatientNumber, p.FirstName + " " + p.LastName, p.IsActive ? "Active" : "Archived", DateLabel(p.CreatedAtUtc) }).ToArray()) with { PatientIds = rows.Take(PageSize).Select(p => p.Id).ToArray() });
    }

    private async Task<HospitalReport> AppointmentReport(ReportKind kind, ReportFilter f, DateTime from, DateTime to,
        Guid? actor, bool dashboard, CancellationToken ct)
    {
        var query = db.Appointments.AsNoTracking().Where(a => actor == null ||
            (a.Doctor.ApplicationUserId == actor && a.Doctor.IsActive && a.Patient.IsActive));
        if (f.Department is Guid department) query = query.Where(a => a.DepartmentId == department);
        if (f.Doctor is Guid doctor) query = query.Where(a => a.DoctorId == doctor);
        if (Status<AppointmentStatus>(f) is { } status) query = query.Where(a => a.Status == status);
        var today = Boundary(Today);
        var tomorrow = Boundary(Today.AddDays(1));
        var occurs = query.Where(a => a.StartAtUtc >= from && a.StartAtUtc < to);
        var distribution = await occurs.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var groups = new List<ReportGroup> { Group(kind == ReportKind.Appointments ? "Appointments" : "OPD appointment worklist",
            Value("Appointments in period", distribution.Sum(g => g.Count)),
            Value("Today's appointments", await query.CountAsync(a => a.StartAtUtc >= today && a.StartAtUtc < tomorrow, ct)),
            Value("Bookings created in period", await query.CountAsync(a => a.CreatedAtUtc >= from && a.CreatedAtUtc < to, ct))),
            Series("Appointment status", Enum.GetValues<AppointmentStatus>().Select(x =>
                Value(x.ToString(), distribution.SingleOrDefault(g => g.Key == x)?.Count ?? 0))) };
        groups.Add(await Trend("Appointment occurrence by day", occurs.Select(a => a.StartAtUtc), false, ct));
        if (dashboard) return new(kind, "Appointment occurrence date", groups, []);
        var byDepartment = await occurs.GroupBy(a => a.Department.Name).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key).Take(100).ToListAsync(ct);
        var byDoctor = await occurs.GroupBy(a => new { a.DoctorId, a.Doctor.FirstName, a.Doctor.LastName })
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(g => g.Count).ThenBy(g => g.Key.DoctorId).Take(100).ToListAsync(ct);
        groups.Add(Series("Appointments by department (up to 100)", byDepartment.Select(g => Value(g.Key, g.Count))));
        groups.Add(Series("Appointments by doctor (up to 100)", byDoctor.Select(g => Value(g.Key.FirstName + " " + g.Key.LastName, g.Count))));
        var ordered = f.Order == "oldest" ? occurs.OrderBy(a => a.StartAtUtc) : occurs.OrderByDescending(a => a.StartAtUtc);
        var rows = await Page(ordered.ThenBy(a => a.Id), f).Select(a => new { a.StartAtUtc, a.Status, Department = a.Department.Name,
            Doctor = a.Doctor.FirstName + " " + a.Doctor.LastName }).ToArrayAsync(ct);
        return new(kind, "Appointment occurrence date. Bookings created use the original creation timestamp.", groups,
            [await Departments(f, ct), await Doctors(f, actor, ct), EnumChoice<AppointmentStatus>("Appointment status", f.Status), DateOrder(f)],
            Table("Appointment occurrences", ["Start (Colombo)", "Department", "Doctor", "Status"],
                rows.Select(a => new[] { DateLabel(a.StartAtUtc), a.Department, a.Doctor, a.Status.ToString() }).ToArray()));
    }
}

