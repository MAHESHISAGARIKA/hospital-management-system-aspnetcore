using System.Security.Claims;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Staff;

public sealed class StaffFault(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}

public sealed class StaffService(HospitalDbContext db, IAuditLogService audit, TimeProvider clock)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(AppointmentService.ToColombo(Now));

    public static IQueryable<StaffDepartmentCount> CurrentDepartmentCounts(HospitalDbContext database)
        => database.Set<StaffDepartment>().AsNoTracking()
            .Where(assignment => assignment.UntilUtc == null)
            .GroupBy(assignment => assignment.Department.Name)
            .OrderBy(group => group.Key)
            .Select(group => new StaffDepartmentCount(group.Key, group.Count()));

    public async Task<Guid> Actor(ClaimsPrincipal user, bool admin, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || admin && !user.IsInRole("Administrator") ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !await db.Users.AnyAsync(x => x.Id == id && x.IsEnabled, ct))
            throw new UnauthorizedAccessException();
        return id;
    }

    public async Task<Employee> Mine(ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = await Actor(user, false, ct);
        return await db.Set<Employee>().SingleOrDefaultAsync(x => x.Active && x.UserId == actor, ct)
            ?? throw new UnauthorizedAccessException();
    }

    public async Task<Guid> SaveEmployee(ClaimsPrincipal user, Guid? id, Guid token, string name, string email,
        string job, DateOnly joined, bool active, Guid? account, CancellationToken ct)
    {
        var actor = await Actor(user, true, ct);
        name = Required(name, 120, "Name");
        email = Required(email, 254, "Email");
        job = Required(job, 100, "Job");
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new StaffFault("Enter a valid work email address.", "Email");
        if (joined.Year < 1900 || joined > Today)
            throw new StaffFault("Choose a joining date between 1900 and today (Asia/Colombo).", "From");
        var row = id.HasValue ? await db.Set<Employee>().FindAsync([id.Value], ct) : new Employee
        {
            Id = Guid.CreateVersion7(), CreatedAtUtc = Now, CreatedBy = actor
        };
        if (row is null) throw new StaffFault("Employee was not found.");
        if (id.HasValue) Check(token, row.Token);
        if (account.HasValue && (!await db.Users.AnyAsync(x => x.Id == account && x.IsEnabled, ct) ||
            await db.Set<Employee>().AnyAsync(x => x.UserId == account && x.Id != row.Id, ct)))
            throw new StaffFault("Choose an enabled account that is not linked to another employee.", "Account");
        if ((!active || account != row.UserId) && await db.Set<StaffAttendance>().AnyAsync(x => x.EmployeeId == row.Id && x.OutUtc == null, ct))
            throw new StaffFault("Close the employee?s current attendance before changing access.");
        if (!id.HasValue)
        {
            row.Number = $"EMP-{row.Id:N}";
            db.Add(row);
        }
        (row.Name, row.WorkEmail, row.JobTitle, row.Joined, row.Active, row.UserId) = (name, email, job, joined, active, account);
        await Save(row, actor, id.HasValue ? "StaffProfileUpdated" : "StaffRegistered", ct);
        return row.Id;
    }

    public async Task Transfer(ClaimsPrincipal user, Guid id, Guid token, Guid department, string reason, CancellationToken ct)
    {
        var actor = await Actor(user, true, ct);
        var employee = await EmployeeAsync(id, ct);
        Check(token, employee.Token);
        if (!employee.Active || !await db.Departments.AnyAsync(x => x.Id == department && x.IsActive, ct))
            throw new StaffFault("Choose an active employee and department.");
        reason = Required(reason, 500);
        var current = await db.Set<StaffDepartment>().SingleOrDefaultAsync(x => x.EmployeeId == id && x.UntilUtc == null, ct);
        var now = Now;
        if (current?.DepartmentId == department || current != null && current.FromUtc.AddTicks(10) > now)
            throw new StaffFault("Choose a different department and retry after the current assignment starts.");
        // The exclusion constraint is deferred so closing and opening can share one save.
        if (current != null) current.UntilUtc = now;
        db.Add(new StaffDepartment { Id = Guid.CreateVersion7(), EmployeeId = id, DepartmentId = department, FromUtc = now, ActorId = actor, Reason = reason });
        await Save(employee, actor, "StaffDepartmentAssigned", ct);
    }

    public async Task Clock(ClaimsPrincipal user, bool start, CancellationToken ct)
    {
        var employee = await Mine(user, ct);
        var current = await db.Set<StaffAttendance>().SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.OutUtc == null, ct);
        var now = Now;
        if (start)
        {
            if (current != null || await db.Set<StaffAttendance>().AnyAsync(x => x.EmployeeId == employee.Id && x.OutUtc > now, ct))
                throw new StaffFault("An attendance session is already open or overlaps this time.");
            db.Add(new StaffAttendance { Id = Guid.CreateVersion7(), EmployeeId = employee.Id, InUtc = now, ActorId = employee.UserId!.Value });
        }
        else
        {
            if (current == null || current.InUtc.AddTicks(10) > now) throw new StaffFault("A prior clock-in is required.");
            current.OutUtc = now;
        }
        await Save(employee, employee.UserId!.Value, start ? "StaffClockedIn" : "StaffClockedOut", ct);
    }

    public async Task Correct(ClaimsPrincipal user, Guid id, Guid token, DateTime start, DateTime end, string reason, CancellationToken ct)
    {
        var actor = await Actor(user, true, ct);
        reason = Required(reason, 500);
        start = LocalToUtc(start);
        end = LocalToUtc(end);
        var session = await db.Set<StaffAttendance>().FindAsync([id], ct) ?? throw new StaffFault("Session was not found.");
        var employee = await EmployeeAsync(session.EmployeeId, ct);
        Check(token, employee.Token);
        if (session.OutUtc is null) throw new StaffFault("Clock out before correcting this session.");
        if (end <= start || end > Now || await db.Set<StaffAttendance>().AnyAsync(x => x.EmployeeId == employee.Id && x.Id != id &&
            x.InUtc < end && (x.OutUtc == null || x.OutUtc > start), ct))
            throw new StaffFault("Enter a completed interval that does not overlap another session.");
        db.Add(new StaffAttendanceCorrection { Id = Guid.CreateVersion7(), AttendanceId = id, PriorInUtc = session.InUtc,
            PriorOutUtc = session.OutUtc, InUtc = start, OutUtc = end, Reason = reason, ActorId = actor, AtUtc = Now });
        (session.InUtc, session.OutUtc) = (start, end);
        await Save(employee, actor, "StaffAttendanceCorrected", ct);
    }

    public async Task SaveType(ClaimsPrincipal user, Guid? id, Guid token, string code, string name, bool active, CancellationToken ct)
    {
        var actor = await Actor(user, true, ct);
        code = Required(code, 30).ToUpperInvariant();
        name = Required(name, 100);
        if (await db.Set<StaffLeaveType>().AnyAsync(x => x.Code == code && x.Id != id, ct))
            throw new StaffFault("That leave code already exists.");
        var row = id.HasValue ? await db.Set<StaffLeaveType>().FindAsync([id.Value], ct) : new StaffLeaveType { Id = Guid.CreateVersion7() };
        if (row is null) throw new StaffFault("Leave category was not found.");
        if (id.HasValue) Check(token, row.Token);
        else db.Add(row);
        (row.Code, row.Name, row.Active, row.Token) = (code, name, active, Guid.CreateVersion7());
        await Persist("StaffLeaveTypeSaved", actor, row.Id, ct);
    }

    public async Task Request(ClaimsPrincipal user, Guid? id, Guid token, Guid type, DateOnly from, DateOnly through, string reason, CancellationToken ct)
    {
        var employee = await Mine(user, ct);
        reason = Required(reason, 500);
        if (from < Today || through < from || through.Year > 9998 ||
            !await db.Set<StaffLeaveType>().AnyAsync(x => x.Id == type && x.Active, ct))
            throw new StaffFault("Select an active leave type and an inclusive date range starting today or later.");
        if (await db.Set<StaffLeaveRequest>().AnyAsync(x => x.EmployeeId == employee.Id && x.Id != id &&
            (x.Status == StaffLeaveStatus.Pending || x.Status == StaffLeaveStatus.Approved) && x.From <= through && x.Through >= from, ct))
            throw new StaffFault("The dates overlap another pending or approved request.");
        var row = id.HasValue ? await db.Set<StaffLeaveRequest>().FindAsync([id.Value], ct) : new StaffLeaveRequest
        { Id = Guid.CreateVersion7(), EmployeeId = employee.Id, ActorId = employee.UserId!.Value, CreatedAtUtc = Now };
        if (row is null || row.EmployeeId != employee.Id) throw new UnauthorizedAccessException();
        if (row.Status != StaffLeaveStatus.Pending) throw new StaffFault("Only pending requests can be edited.");
        if (id.HasValue) Check(token, row.Token);
        else db.Add(row);
        (row.TypeId, row.From, row.Through, row.Reason, row.Token) = (type, from, through, reason, Guid.CreateVersion7());
        await Save(employee, employee.UserId!.Value, id.HasValue ? "StaffLeaveEdited" : "StaffLeaveSubmitted", ct);
    }

    public async Task<string?> Conflict(Employee employee, DateOnly from, DateOnly through, CancellationToken ct)
    {
        if (!employee.UserId.HasValue) return null;
        var start = AppointmentService.ToUtc(from, TimeOnly.MinValue);
        var stop = AppointmentService.ToUtc(through.AddDays(1), TimeOnly.MinValue);
        if (await db.Appointments.AnyAsync(x => x.Doctor.ApplicationUserId == employee.UserId && x.StartAtUtc < stop && x.EndAtUtc > start &&
            (x.Status == AppointmentStatus.Scheduled || x.Status == AppointmentStatus.CheckedIn), ct))
            return "Scheduled or checked-in appointments must be resolved before approval.";
        if (await db.InpatientAdmissions.AnyAsync(x => x.Status == AdmissionStatus.Admitted && x.Doctor.ApplicationUserId == employee.UserId, ct) ||
            await db.InpatientCareAssignments.AnyAsync(x => x.NurseUserId == employee.UserId && x.EndedAtUtc == null, ct))
            return "An active inpatient care responsibility must be resolved before approval.";
        return null;
    }

    public async Task Decide(ClaimsPrincipal user, Guid id, Guid token, StaffLeaveStatus next, string? reason, CancellationToken ct)
    {
        if (next is not (StaffLeaveStatus.Approved or StaffLeaveStatus.Rejected or StaffLeaveStatus.Withdrawn))
            throw new StaffFault("Choose a valid decision.");
        var actor = await Actor(user, next != StaffLeaveStatus.Withdrawn, ct);
        var request = await db.Set<StaffLeaveRequest>().FindAsync([id], ct) ?? throw new UnauthorizedAccessException();
        var employee = await EmployeeAsync(request.EmployeeId, ct);
        if (next == StaffLeaveStatus.Withdrawn ? employee.UserId != actor || !employee.Active :
            employee.UserId == actor || request.ActorId == actor)
            throw new UnauthorizedAccessException();
        Check(token, request.Token);
        if (request.Status != StaffLeaveStatus.Pending) throw new StaffFault("Only pending requests can receive a decision.");
        if (next == StaffLeaveStatus.Rejected) reason = Required(reason, 500);
        else reason = null;
        if (next == StaffLeaveStatus.Approved)
        {
            if (!employee.Active || request.From < Today) throw new StaffFault("The employee or request dates are no longer eligible.");
            var conflict = await Conflict(employee, request.From, request.Through, ct);
            if (conflict != null) throw new StaffFault(conflict);
        }
        (request.Status, request.Token) = (next, Guid.CreateVersion7());
        db.Add(new StaffLeaveDecision { Id = Guid.CreateVersion7(), RequestId = id, ActorId = actor, Status = next, AtUtc = Now, Reason = reason });
        await Save(employee, actor, $"StaffLeave{next}", ct);
    }

    public async Task<Employee> EmployeeAsync(Guid id, CancellationToken ct)
        => await db.Set<Employee>().FindAsync([id], ct) ?? throw new StaffFault("Employee was not found.");

    private async Task Save(Employee employee, Guid actor, string action, CancellationToken ct)
    {
        (employee.Token, employee.UpdatedBy, employee.UpdatedAtUtc) = (Guid.CreateVersion7(), actor, Now);
        await Persist(action, actor, employee.Id, ct);
    }
    private async Task Persist(string action, Guid actor, Guid id, CancellationToken ct)
    {
        try { await audit.WriteAsync(action, actor, null, $"Staff entity {id:D}", ct); }
        catch (Exception error) when (error is DbUpdateException ||
            error is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ExclusionViolation })
        {
            db.ChangeTracker.Clear();
            throw new StaffFault("This change conflicts with a saved record or a concurrent update. Reload and try again.");
        }
    }
    private static void Check(Guid token, Guid current)
    {
        if (token == Guid.Empty || token != current) throw new StaffFault("This record changed. Reload the page.");
    }
    private static string Required(string? value, int size, string? field = null) => value?.Trim() is { Length: > 0 } text && text.Length <= size
        ? text : throw new StaffFault($"Enter a value of up to {size} characters.", field);
    private static DateTime LocalToUtc(DateTime value)
    {
        if (value.Year is < 1900 or > 9998) throw new StaffFault("Choose a valid local date.");
        return AppointmentService.ToUtc(DateOnly.FromDateTime(value), TimeOnly.FromDateTime(value));
    }
}
