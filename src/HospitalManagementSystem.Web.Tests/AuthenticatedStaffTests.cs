using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class AuthenticatedStaffTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    private const string Password = "Synthetic!Staff678";
    private const string PrivateReason = "Fictional private leave reason";
    private static DateOnly LeaveDay => DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow)).AddDays(3);

    [Fact]
    public async Task AddStaffAcceptsEmptyHiddenTokenAndOptionalAccount()
    {
        using var isolated = new AuthenticationTestApplication();
        using var admin = await SignIn(HospitalRoles.Administrator, isolated: isolated);
        var fields = EmployeeForm(Guid.Empty);
        fields["Token"] = "";
        fields["Account"] = "";
        fields["From"] = "2026-02-19";

        await Saved(admin, "/Staff/Employees/Save", "/Staff", fields);
        await using var scope = isolated.Services.CreateAsyncScope();
        var employee = await Db(scope).Set<Employee>().SingleAsync();
        Assert.Null(employee.UserId);
        Assert.Equal(new DateOnly(2026, 2, 19), employee.Joined);
    }

    [Theory]
    [InlineData("Name", "", "Enter the employee")]
    [InlineData("Email", "invalid-email", "Enter a valid work email address.")]
    [InlineData("From", "2099-01-01", "Choose a joining date between 1900 and today")]
    public async Task InvalidAddStaffKeepsFormOpenAndPreservesOtherValues(string field, string value, string message)
    {
        using var isolated = new AuthenticationTestApplication();
        using var admin = await SignIn(HospitalRoles.Administrator, isolated: isolated);
        var fields = EmployeeForm(Guid.Empty);
        fields["Account"] = "";
        fields["Name"] = "Fictional Staff Form";
        fields["Job"] = "Fictional Receptionist";
        fields["Active"] = "false";
        fields[field] = value;

        using var response = await Post(admin, "/Staff/Employees/Save", "/Staff", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("<details[^>]*open[^>]*>\\s*<summary>Add Staff", html);
        var fieldError = Regex.Match(html, $"""<span[^>]*data-valmsg-for="{field}"[^>]*>([^<]+)</span>""");
        Assert.True(fieldError.Success);
        Assert.Contains(message, WebUtility.HtmlDecode(fieldError.Groups[1].Value));
        Assert.Contains("Fictional Receptionist", html);
        Assert.Matches("""<option(?=[^>]*value="false")(?=[^>]*selected)[^>]*>Inactive""", html);
        if (field == "Email") Assert.Contains("invalid-email", html);
        if (field == "From") Assert.Contains("2099-01-01", html);
        await using var scope = isolated.Services.CreateAsyncScope();
        Assert.False(await Db(scope).Set<Employee>().AnyAsync());
    }

    [Fact]
    public void StaffDepartmentSummaryTranslatesForPostgreSqlWithoutConnecting()
    {
        using var database = new HospitalDbContext(new DbContextOptionsBuilder<HospitalDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=design_only").Options);
        var sql = StaffService.CurrentDepartmentCounts(database).ToQueryString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("ORDER BY", sql);
    }

    [Fact]
    public async Task AdministratorSeesStaffEmptyStateAndExistingRegistrationForm()
    {
        using var isolated = new AuthenticationTestApplication();
        using var admin = await SignIn(HospitalRoles.Administrator, isolated: isolated);
        var page = await admin.Http.GetStringAsync("/Staff");

        Assert.Contains("No staff members have been registered yet.", page);
        Assert.Contains("Add Staff", page);
        Assert.Contains("/Staff/Employees/Save", page);
        Assert.Contains("__RequestVerificationToken", page);
    }

    [Fact]
    public async Task AdministratorRegistersAndUpdatesEmployee()
    {
        using var admin = await SignIn(HospitalRoles.Administrator);
        using var target = await SignIn(HospitalRoles.Nurse);
        var fields = EmployeeForm(target.User.Id);
        await Saved(admin, "/Staff/Employees/Save", "/Staff", fields);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var registered = await db.Set<Employee>().AsNoTracking().SingleAsync(e => e.UserId == target.User.Id);
        Assert.StartsWith("EMP-", registered.Number);
        Assert.Equal(admin.User.Id, registered.CreatedBy);
        Assert.Contains(registered.Name, await admin.Http.GetStringAsync($"/Staff/Employees/{registered.Id}"));

        fields["Token"] = registered.Token.ToString();
        fields["Name"] = "Updated fictional employee";
        fields["Active"] = "false";
        await Saved(admin, $"/Staff/Employees/Save/{registered.Id}", "/Staff", fields);
        var updated = await db.Set<Employee>().AsNoTracking().SingleAsync(e => e.Id == registered.Id);
        Assert.Equal(fields["Name"], updated.Name);
        Assert.Equal(registered.Number, updated.Number);
        Assert.NotEqual(registered.Token, updated.Token);
        Assert.False(updated.Active);
        await Denied(target.Http.GetAsync("/Staff/My"));
    }

    [Fact]
    public async Task LinkedEmployeeIsScopedToOwnProfileAndCannotManageEmployees()
    {
        using var employee = await SignIn(HospitalRoles.Nurse, linked: true);
        using var other = await SignIn(HospitalRoles.Doctor, linked: true);
        var html = await employee.Http.GetStringAsync("/Staff/My");
        Assert.Contains(employee.Profile!.Name, html);
        Assert.DoesNotContain(other.Profile!.Name, html);

        foreach (var path in new[] { "/Staff", "/Staff/Reports", "/Staff/Attendance", "/Staff/Leave/Approvals",
            $"/Staff/Employees/{other.Profile.Id}", $"/Staff/My?id={other.Profile.Id}" })
            await Denied(employee.Http.GetAsync(path));

        var fields = EmployeeForm(other.User.Id);
        fields["Token"] = other.Profile.Token.ToString();
        await Denied(Post(employee, $"/Staff/Employees/Save/{other.Profile.Id}", "/Staff/My", fields));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(other.Profile.Name, (await Db(scope).Set<Employee>().FindAsync(other.Profile.Id))!.Name);
    }

    [Fact]
    public async Task EnabledButUnlinkedAccountCannotUseSelfService()
    {
        using var user = await SignIn(HospitalRoles.Nurse);
        await Denied(user.Http.GetAsync("/Staff/My"));
        await Denied(Post(user, "/Staff/Attendance/ClockIn", "/Dashboard"));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await Db(scope).Set<StaffAttendance>().AnyAsync(a => a.ActorId == user.User.Id));
    }

    [Fact]
    public async Task EmployeeCanClockInAndOutButCannotClockInTwice()
    {
        using var employee = await SignIn(HospitalRoles.Nurse, linked: true);
        await Saved(employee, "/Staff/Attendance/ClockIn", "/Staff/My");
        using var duplicate = await Post(employee, "/Staff/Attendance/ClockIn", "/Staff/My");
        Assert.Contains("already open", await RedirectPage(employee, duplicate));

        await using var scope = factory.Services.CreateAsyncScope();
        var query = Db(scope).Set<StaffAttendance>().AsNoTracking().Where(a => a.EmployeeId == employee.Profile!.Id);
        var open = await query.SingleAsync();
        Assert.Equal(employee.User.Id, open.ActorId);
        Assert.Null(open.OutUtc);

        await Saved(employee, "/Staff/Attendance/ClockOut", "/Staff/My");
        var closed = await query.SingleAsync();
        Assert.Equal(open.Id, closed.Id);
        Assert.True(closed.OutUtc > closed.InUtc);
    }

    [Fact]
    public async Task LeaveSubmissionIsPrivateAndOtherEmployeesCannotEditWithdrawOrApproveIt()
    {
        using var owner = await SignIn(HospitalRoles.Nurse, linked: true);
        using var other = await SignIn(HospitalRoles.Nurse, linked: true);
        var leave = await SubmitLeave(owner);
        Assert.Equal(StaffLeaveStatus.Pending, leave.Status);
        Assert.Equal(owner.User.Id, leave.ActorId);
        Assert.Equal(PrivateReason, leave.Reason);
        Assert.Contains(PrivateReason, await owner.Http.GetStringAsync("/Staff/My"));
        Assert.DoesNotContain(PrivateReason, await other.Http.GetStringAsync("/Staff/My"));

        var fields = LeaveForm(leave.TypeId);
        fields["Token"] = leave.Token.ToString();
        fields["Reason"] = "Attempted change";
        await Denied(Post(other, $"/Staff/Leave/Request/{leave.Id}", "/Staff/My", fields));
        await Denied(Post(other, $"/Staff/Leave/Withdraw/{leave.Id}", "/Staff/My", fields));
        await Denied(Post(other, $"/Staff/Leave/Approve/{leave.Id}", "/Staff/My", fields));
        await AssertPending(leave);

        await using var scope = factory.Services.CreateAsyncScope();
        var entries = await Db(scope).AuditLogEntries.Where(a => a.ActorUserId == owner.User.Id).ToListAsync();
        Assert.Contains(entries, a => a.Action == "StaffLeaveSubmitted");
        Assert.All(entries, entry => Assert.DoesNotContain(PrivateReason, entry.Details ?? ""));
    }

    [Fact]
    public async Task AdministratorCannotApproveOwnLeaveButAnotherAdministratorCan()
    {
        using var owner = await SignIn(HospitalRoles.Administrator, linked: true);
        using var reviewer = await SignIn(HospitalRoles.Administrator);
        var leave = await SubmitLeave(owner);
        var url = $"/Staff/Leave/Approve/{leave.Id}";
        await Denied(Post(owner, url, "/Staff/My", Token(leave.Token)));
        await AssertPending(leave);

        await Saved(reviewer, url, "/Staff", Token(leave.Token));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        Assert.Equal(StaffLeaveStatus.Approved, (await db.Set<StaffLeaveRequest>().FindAsync(leave.Id))!.Status);
        var decision = await db.Set<StaffLeaveDecision>().SingleAsync(d => d.RequestId == leave.Id);
        Assert.Equal(reviewer.User.Id, decision.ActorId);
        Assert.Equal(StaffLeaveStatus.Approved, decision.Status);
        Assert.True(decision.AtUtc >= leave.CreatedAtUtc);
    }

    [Theory]
    [InlineData("appointment")]
    [InlineData("admitting doctor")]
    [InlineData("assigned nurse")]
    public async Task DirectApprovalCannotBypassClinicalConflict(string conflict)
    {
        using var reviewer = await SignIn(HospitalRoles.Administrator);
        using var owner = await SignIn(conflict == "assigned nurse" ? HospitalRoles.Nurse : HospitalRoles.Doctor, linked: true);
        var leave = await SubmitLeave(owner);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var doctor = await AddDoctor(db, conflict == "assigned nurse" ? null : owner.User.Id);
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = Guid.NewGuid().ToString("N"),
            FirstName = "Fictional", LastName = "Patient", Gender = "Unknown" };
        db.Patients.Add(patient);
        if (conflict == "appointment")
        {
            var start = AppointmentService.ToUtc(leave.From, new TimeOnly(10, 0));
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), AppointmentNumber = Guid.NewGuid().ToString("N"),
                PatientId = patient.Id, DoctorId = doctor.Id, DepartmentId = doctor.DepartmentId, StartAtUtc = start,
                EndAtUtc = start.AddMinutes(30), Status = AppointmentStatus.Scheduled });
        }
        else
        {
            var admission = new InpatientAdmission { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
                Number = Guid.NewGuid().ToString("N"), Status = AdmissionStatus.Admitted };
            db.InpatientAdmissions.Add(admission);
            if (conflict == "assigned nurse")
                db.InpatientCareAssignments.Add(new InpatientCareAssignment { Id = Guid.NewGuid(),
                    AdmissionId = admission.Id, NurseUserId = owner.User.Id });
        }
        await db.SaveChangesAsync();

        using var response = await Post(reviewer, $"/Staff/Leave/Approve/{leave.Id}", "/Staff", Token(leave.Token));
        Assert.Contains(conflict == "appointment" ? "appointments must be resolved" : "inpatient care responsibility",
            await RedirectPage(reviewer, response));
        await AssertPending(leave);
    }

    [Fact]
    public async Task AttendanceCorrectionRequiresAdministratorAndPreservesOriginalTimes()
    {
        using var admin = await SignIn(HospitalRoles.Administrator);
        using var employee = await SignIn(HospitalRoles.Nurse, linked: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var priorIn = DateTime.UtcNow.Date.AddDays(-2).AddHours(3);
        var session = new StaffAttendance { Id = Guid.NewGuid(), EmployeeId = employee.Profile!.Id, InUtc = priorIn,
            OutUtc = priorIn.AddHours(5), ActorId = employee.User.Id };
        db.Add(session);
        await db.SaveChangesAsync();

        var correctedIn = priorIn.AddMinutes(15);
        var fields = Token(employee.Profile.Token);
        fields["Start"] = LocalInput(correctedIn);
        fields["End"] = LocalInput(session.OutUtc.Value);
        fields["Reason"] = "Fictional missed timestamp";
        var path = $"/Staff/Attendance/Correct/{session.Id}";
        await Denied(Post(employee, path, "/Staff/My", fields));
        Assert.Empty(await db.Set<StaffAttendanceCorrection>().Where(c => c.AttendanceId == session.Id).ToListAsync());

        await Saved(admin, path, "/Staff", fields);
        var history = await db.Set<StaffAttendanceCorrection>().SingleAsync(c => c.AttendanceId == session.Id);
        Assert.Equal(priorIn, history.PriorInUtc);
        Assert.Equal(session.OutUtc, history.PriorOutUtc);
        Assert.Equal(correctedIn, history.InUtc);
        Assert.Equal(admin.User.Id, history.ActorId);
        Assert.Equal(fields["Reason"], history.Reason);
        Assert.Equal(correctedIn, (await db.Set<StaffAttendance>().AsNoTracking().SingleAsync(a => a.Id == session.Id)).InUtc);
    }

    [Fact]
    public async Task DirectCorrectionCannotCloseAnOpenAttendanceSession()
    {
        using var admin = await SignIn(HospitalRoles.Administrator);
        using var employee = await SignIn(HospitalRoles.Nurse, linked: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var start = DateTime.UtcNow.Date.AddDays(-2);
        var session = new StaffAttendance { Id = Guid.NewGuid(), EmployeeId = employee.Profile!.Id,
            InUtc = start, ActorId = employee.User.Id };
        db.Add(session);
        await db.SaveChangesAsync();

        var fields = Token(employee.Profile.Token);
        fields["Start"] = LocalInput(start);
        fields["End"] = LocalInput(start.AddHours(1));
        fields["Reason"] = "Fictional correction";
        using var response = await Post(admin, $"/Staff/Attendance/Correct/{session.Id}", "/Staff", fields);
        Assert.Contains("Clock out before correcting this session.", await RedirectPage(admin, response));

        Assert.Null((await db.Set<StaffAttendance>().AsNoTracking().SingleAsync(a => a.Id == session.Id)).OutUtc);
        Assert.False(await db.Set<StaffAttendanceCorrection>().AnyAsync(c => c.AttendanceId == session.Id));
    }

    [Fact]
    public async Task DepartmentTransferPreservesHistoryAndClinicalDepartmentAndSchedule()
    {
        using var admin = await SignIn(HospitalRoles.Administrator);
        using var employee = await SignIn(HospitalRoles.Doctor, linked: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var doctor = await AddDoctor(db, employee.User.Id);
        var destination = AddDepartment(db);
        var start = new TimeOnly(8, 0);
        var schedule = new DoctorSchedule { Id = Guid.NewGuid(), DoctorId = doctor.Id, DayOfWeek = 1,
            StartTime = start, EndTime = start.AddHours(4), IsActive = true, ConcurrencyToken = Guid.NewGuid() };
        var old = new StaffDepartment { Id = Guid.NewGuid(), DepartmentId = doctor.DepartmentId,
            EmployeeId = employee.Profile!.Id, FromUtc = DateTime.UtcNow.AddDays(-7), ActorId = admin.User.Id };
        db.AddRange(schedule, old);
        await db.SaveChangesAsync();

        var fields = Token(employee.Profile.Token);
        fields["Department"] = destination.Id.ToString();
        fields["Reason"] = "Fictional HR transfer";
        await Saved(admin, $"/Staff/Departments/Transfer/{employee.Profile.Id}", "/Staff", fields);

        var assignments = await db.Set<StaffDepartment>().AsNoTracking().Where(a => a.EmployeeId == employee.Profile.Id)
            .OrderBy(a => a.FromUtc).ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.Equal(old.Id, assignments[0].Id);
        Assert.Equal(old.DepartmentId, assignments[0].DepartmentId);
        Assert.Equal(assignments[1].FromUtc, assignments[0].UntilUtc);
        Assert.Equal(destination.Id, assignments[1].DepartmentId);
        Assert.Null(assignments[1].UntilUtc);
        Assert.Equal(doctor.DepartmentId, (await db.Doctors.AsNoTracking().SingleAsync(d => d.Id == doctor.Id)).DepartmentId);
        var unchanged = await db.Set<DoctorSchedule>().AsNoTracking().SingleAsync(s => s.DoctorId == doctor.Id);
        Assert.Equal((schedule.Id, schedule.DayOfWeek, schedule.StartTime, schedule.EndTime, schedule.IsActive, schedule.ConcurrencyToken),
            (unchanged.Id, unchanged.DayOfWeek, unchanged.StartTime, unchanged.EndTime, unchanged.IsActive, unchanged.ConcurrencyToken));
    }

    [Fact]
    public async Task AuthorizedStaffPostsRejectMissingAntiforgeryTokens()
    {
        using var admin = await SignIn(HospitalRoles.Administrator, linked: true);
        var id = Guid.NewGuid();
        foreach (var path in new[] { "/Staff/Employees/Save", $"/Staff/Departments/Transfer/{id}",
            "/Staff/Attendance/ClockIn", "/Staff/Attendance/ClockOut", $"/Staff/Attendance/Correct/{id}",
            "/Staff/Leave/Types", "/Staff/Leave/Request", $"/Staff/Leave/Withdraw/{id}",
            $"/Staff/Leave/Approve/{id}", $"/Staff/Leave/Reject/{id}" })
        {
            using var response = await admin.Http.PostAsync(path, new FormUrlEncodedContent([]));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} returned {response.StatusCode}");
        }
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await Db(scope).AuditLogEntries.AnyAsync(a => a.ActorUserId == admin.User.Id && a.Action.StartsWith("Staff")));
    }

    private async Task<StaffLeaveRequest> SubmitLeave(Session employee)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var category = new StaffLeaveType { Id = Guid.NewGuid(), Code = Guid.NewGuid().ToString("N")[..20],
            Name = "Fictional leave category", Active = true, Token = Guid.NewGuid() };
        db.Add(category);
        await db.SaveChangesAsync();
        await Saved(employee, "/Staff/Leave/Request", "/Staff/My", LeaveForm(category.Id));
        return await db.Set<StaffLeaveRequest>().SingleAsync(r => r.EmployeeId == employee.Profile!.Id);
    }

    private async Task AssertPending(StaffLeaveRequest original)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = Db(scope);
        var current = await db.Set<StaffLeaveRequest>().FindAsync(original.Id);
        Assert.NotNull(current);
        Assert.Equal((StaffLeaveStatus.Pending, original.Token, original.Reason), (current.Status, current.Token, current.Reason));
        Assert.False(await db.Set<StaffLeaveDecision>().AnyAsync(d => d.RequestId == original.Id));
    }

    private async Task<Session> SignIn(string role, bool linked = false, AuthenticationTestApplication? isolated = null)
    {
        var application = isolated ?? factory;
        await using var scope = application.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsEnabled = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);

        Employee? profile = null;
        if (linked)
        {
            profile = new Employee { Id = Guid.NewGuid(), UserId = user.Id, Number = $"EMP-{Guid.NewGuid():N}",
                Name = $"Fictional employee {Guid.NewGuid():N}", WorkEmail = email, JobTitle = "Fictional role",
                Joined = LeaveDay.AddYears(-1), Active = true, Token = Guid.NewGuid() };
            Db(scope).Add(profile);
            await Db(scope).SaveChangesAsync();
        }

        var session = new Session(application.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }), user, profile);
        using var login = await Post(session, "/Account/Login", "/Account/Login",
            new Dictionary<string, string> { ["Email"] = email, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Dashboard", login.Headers.Location!.OriginalString);
        return session;
    }

    private static async Task<HttpResponseMessage> Post(Session session, string path, string form,
        Dictionary<string, string>? fields = null)
    {
        using var page = await session.Http.GetAsync(form);
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No antiforgery token from {form} ({page.StatusCode}).");
        var posted = fields == null ? new Dictionary<string, string>() : new(fields);
        posted["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        using var content = new FormUrlEncodedContent(posted);
        return await session.Http.PostAsync(path, content);
    }

    private static async Task Saved(Session session, string path, string form, Dictionary<string, string>? fields = null)
    {
        using var response = await Post(session, path, form, fields);
        Assert.Contains("Change saved.", await RedirectPage(session, response));
    }

    private static async Task<string> RedirectPage(Session session, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Staff", response.Headers.Location!.OriginalString);
        return await session.Http.GetStringAsync(response.Headers.Location);
    }

    private static async Task Denied(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", new Uri(response.RequestMessage!.RequestUri!, response.Headers.Location!).AbsolutePath);
    }

    private static Dictionary<string, string> Token(Guid token) => new() { ["Token"] = token.ToString() };
    private static string DateInput(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string LocalInput(DateTime value) => AppointmentService.ToColombo(value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    private static Dictionary<string, string> EmployeeForm(Guid account) => new()
    {
        ["Account"] = account.ToString(), ["Name"] = $"Fictional registration {Guid.NewGuid():N}",
        ["Email"] = "fictional@example.test", ["Job"] = "Fictional role", ["From"] = DateInput(LeaveDay.AddYears(-1)), ["Active"] = "true"
    };

    private static Dictionary<string, string> LeaveForm(Guid category)
    {
        var day = LeaveDay;
        return new() { ["Type"] = category.ToString(), ["From"] = DateInput(day), ["Through"] = DateInput(day), ["Reason"] = PrivateReason };
    }

    private static HospitalDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<HospitalDbContext>();

    private static Department AddDepartment(HospitalDbContext db)
    {
        var key = Guid.NewGuid().ToString("N");
        var department = new Department { Id = Guid.NewGuid(), DepartmentCode = key[..10], Name = key, IsActive = true };
        db.Add(department);
        return department;
    }

    private static async Task<Doctor> AddDoctor(HospitalDbContext db, Guid? account)
    {
        var key = Guid.NewGuid().ToString("N")[..20];
        var doctor = new Doctor { Id = Guid.NewGuid(), DepartmentId = AddDepartment(db).Id, ApplicationUserId = account,
            DoctorNumber = key, RegistrationNumber = key, FirstName = "Fictional", LastName = "Doctor", Specialization = "Test", IsActive = true };
        db.Add(doctor);
        await db.SaveChangesAsync();
        return doctor;
    }

    private sealed record Session(HttpClient Http, ApplicationUser User, Employee? Profile) : IDisposable
    {
        public void Dispose() => Http.Dispose();
    }
}

