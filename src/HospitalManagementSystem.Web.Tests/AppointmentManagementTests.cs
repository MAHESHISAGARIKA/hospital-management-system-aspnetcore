using System.Net.Http.Json;
using System.Net;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class AppointmentManagementTests : IClassFixture<AuthenticationTestApplication>
{
    private readonly AuthenticationTestApplication _factory;

    public AppointmentManagementTests(AuthenticationTestApplication factory) => _factory = factory;

    [Fact]
    public async Task BookingCreatesUtcAppointmentWithUniqueNumberAndAudit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);

        var appointment = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-success"), Guid.CreateVersion7());

        Assert.StartsWith("APT-", appointment.AppointmentNumber);
        Assert.Equal(DateTimeKind.Utc, appointment.StartAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, appointment.EndAtUtc.Kind);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "AppointmentCreated");
    }

    [Fact]
    public async Task BookingRejectsArchivedPatientAndInactiveDoctor()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);

        var patient = await db.Patients.FindAsync(fixture.PatientId);
        patient!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.BookAsync(
            Booking(fixture, date, new TimeOnly(10, 0), "request-archived"), Guid.CreateVersion7()));

        patient.IsActive = true;
        var doctor = await db.Doctors.FindAsync(fixture.DoctorId);
        doctor!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.BookAsync(
            Booking(fixture, date, new TimeOnly(10, 0), "request-inactive-doctor"), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task BookingRejectsInvalidPatientAndOutsideSchedule()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);

        var invalidPatient = Booking(fixture, date, new TimeOnly(10, 0), "request-invalid-patient");
        invalidPatient.PatientId = Guid.CreateVersion7();
        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.BookAsync(invalidPatient, Guid.CreateVersion7()));

        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.BookAsync(
            Booking(fixture, date, new TimeOnly(8, 30), "request-outside"), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task OverlappingBookingIsRejectedButAdjacentBookingIsAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);

        await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-first", 30), Guid.CreateVersion7());
        await Assert.ThrowsAsync<AppointmentConflictException>(() => service.BookAsync(
            Booking(fixture, date, new TimeOnly(10, 15), "request-overlap", 30), Guid.CreateVersion7()));

        var adjacent = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 30), "request-adjacent", 30), Guid.CreateVersion7());
        Assert.Equal(new TimeOnly(11, 0), TimeOnly.FromDateTime(AppointmentService.ToColombo(adjacent.StartAtUtc).AddMinutes(30)));
    }

    [Fact]
    public async Task IdempotentRetryReturnsSameBookingAndRejectsChangedPayload()
    {
        using var scope = _factory.Services.CreateScope();
        var fixture = await CreateFixtureAsync(scope.ServiceProvider.GetRequiredService<HospitalDbContext>());
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var model = Booking(fixture, date, new TimeOnly(10, 0), "same-request-key");

        var first = await service.BookAsync(model, Guid.CreateVersion7());
        var retry = await service.BookAsync(model, first.CreatedByUserId);
        Assert.Equal(first.Id, retry.Id);

        var altered = Booking(fixture, date, new TimeOnly(10, 30), "same-request-key");
        await Assert.ThrowsAsync<AppointmentIdempotencyConflictException>(() => service.BookAsync(altered, first.CreatedByUserId));
    }

    [Fact]
    public async Task BookingRetryMatchesOriginalNormalizedPayloadAfterRescheduling()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var actor = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");
        var original = Booking(fixture, FutureWeekday(8), new TimeOnly(10, 0), key);
        original.ReasonForVisit = " Follow-up ";
        original.Notes = " Morning preferred ";
        var booked = await service.BookAsync(original, actor);

        await service.RescheduleAsync(booked.Id, new AppointmentRescheduleViewModel
        {
            DoctorId = fixture.DoctorId, DepartmentId = fixture.DepartmentId,
            AppointmentDate = original.AppointmentDate, StartTime = new TimeOnly(11, 0),
            DurationMinutes = 30, ConcurrencyToken = booked.ConcurrencyToken
        }, actor);

        original.ReasonForVisit = "Follow-up";
        original.Notes = "Morning preferred";
        var retry = await service.BookAsync(original, actor);
        Assert.Equal(booked.Id, retry.Id);
        Assert.Equal(new TimeOnly(11, 0), TimeOnly.FromDateTime(AppointmentService.ToColombo(retry.StartAtUtc)));

        original.ReasonForVisit = "Different reason";
        await Assert.ThrowsAsync<AppointmentIdempotencyConflictException>(() => service.BookAsync(original, actor));
        original.ReasonForVisit = "Follow-up";
        original.Notes = "Different notes";
        await Assert.ThrowsAsync<AppointmentIdempotencyConflictException>(() => service.BookAsync(original, actor));
        var otherActorBooking = await service.BookAsync(original, Guid.NewGuid());
        Assert.NotEqual(booked.Id, otherActorBooking.Id);
    }

    [Fact]
    public async Task AvailabilityAlignsScheduleBoundariesAndExcludesOnlyTheRescheduledAppointment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var schedule = await db.DoctorSchedules.SingleAsync(item => item.DoctorId == fixture.DoctorId);
        schedule.StartTime = new TimeOnly(9, 10);
        schedule.EndTime = new TimeOnly(12, 0);
        await db.SaveChangesAsync();
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var booked = await service.BookAsync(
            Booking(fixture, date, new TimeOnly(9, 30), Guid.NewGuid().ToString("N")), Guid.NewGuid());

        var slots = await service.GetAvailableSlotsAsync(fixture.DoctorId, date, 30);
        Assert.Equal(new TimeOnly(10, 0), slots.First());
        Assert.Equal(new TimeOnly(11, 30), slots.Last());
        var rescheduleSlots = await service.GetAvailableSlotsAsync(fixture.DoctorId, date, 30, excludingAppointmentId: booked.Id);
        Assert.Equal(new TimeOnly(9, 15), rescheduleSlots.First());
        Assert.Contains(new TimeOnly(9, 30), rescheduleSlots);
        Assert.DoesNotContain(new TimeOnly(9, 10), rescheduleSlots);
        Assert.DoesNotContain(new TimeOnly(11, 45), rescheduleSlots);
        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.GetAvailableSlotsAsync(fixture.DoctorId, date, 20));
        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.BookAsync(
            Booking(fixture, date, new TimeOnly(9, 10), Guid.NewGuid().ToString("N")), Guid.NewGuid()));
    }

    [Fact]
    public async Task CancellationRequiresReasonAndDoesNotDeleteAppointment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var booked = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-cancel"), Guid.CreateVersion7());

        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.CancelAsync(booked.Id,
            new AppointmentCancellationViewModel { Reason = " ", ConcurrencyToken = booked.ConcurrencyToken }, Guid.CreateVersion7()));

        var actor = Guid.CreateVersion7();
        var statusChange = new AppointmentStatusViewModel { Status = AppointmentStatus.Cancelled, ConcurrencyToken = booked.ConcurrencyToken };
        await Assert.ThrowsAsync<AppointmentStatusException>(() => service.ChangeStatusAsync(booked.Id, statusChange, actor));
        statusChange.Status = AppointmentStatus.Completed;
        await Assert.ThrowsAsync<AppointmentStatusException>(() => service.ChangeStatusAsync(booked.Id, statusChange, actor));
        Assert.Equal(AppointmentStatus.Scheduled, booked.Status);

        const string reason = "Synthetic private patient information";
        await service.CancelAsync(booked.Id,
            new AppointmentCancellationViewModel { Reason = reason, ConcurrencyToken = booked.ConcurrencyToken }, actor);

        var cancelled = await db.Appointments.SingleAsync(item => item.Id == booked.Id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Equal(actor, cancelled.CancelledByUserId);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Equal(reason, cancelled.CancellationReason);
        var audit = await db.AuditLogEntries.SingleAsync(entry =>
            entry.Action == "AppointmentCancelled" && entry.ActorUserId == actor);
        Assert.Equal(DateTimeKind.Utc, audit.OccurredAtUtc.Kind);
        Assert.Contains(booked.Id.ToString("D"), audit.Details);
        Assert.DoesNotContain(reason, audit.Details);

        await Assert.ThrowsAsync<AppointmentStatusException>(() => service.CancelAsync(booked.Id,
            new AppointmentCancellationViewModel { Reason = reason, ConcurrencyToken = cancelled.ConcurrencyToken }, actor));

        var replacement = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-slot-reused"), Guid.CreateVersion7());
        Assert.NotEqual(booked.Id, replacement.Id);
    }

    [Fact]
    public async Task AppointmentLifecycleDoesNotAllowTerminalStatusToReturnToScheduled()
    {
        Assert.True(AppointmentService.IsValidTransition(AppointmentStatus.Scheduled, AppointmentStatus.CheckedIn));
        Assert.True(AppointmentService.IsValidTransition(AppointmentStatus.CheckedIn, AppointmentStatus.Completed));
        Assert.True(AppointmentService.IsValidTransition(AppointmentStatus.Scheduled, AppointmentStatus.Missed));
        Assert.False(AppointmentService.IsValidTransition(AppointmentStatus.Completed, AppointmentStatus.Scheduled));
        Assert.False(AppointmentService.IsValidTransition(AppointmentStatus.Cancelled, AppointmentStatus.Scheduled));
        Assert.False(AppointmentService.IsValidTransition(AppointmentStatus.Missed, AppointmentStatus.Scheduled));
    }

    [Fact]
    public async Task RescheduleValidatesScheduleAndConcurrencyToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var booked = await service.BookAsync(Booking(fixture, FutureWeekday(8), new TimeOnly(10, 0), "request-reschedule"), Guid.CreateVersion7());

        var stale = new AppointmentRescheduleViewModel
        {
            DoctorId = fixture.DoctorId,
            DepartmentId = fixture.DepartmentId,
            AppointmentDate = FutureWeekday(9),
            StartTime = new TimeOnly(11, 0),
            DurationMinutes = 30,
            ConcurrencyToken = Guid.CreateVersion7()
        };
        await Assert.ThrowsAsync<AppointmentConcurrencyException>(() => service.RescheduleAsync(booked.Id, stale, Guid.CreateVersion7()));

        stale.ConcurrencyToken = booked.ConcurrencyToken;
        var moved = await service.RescheduleAsync(booked.Id, stale, Guid.CreateVersion7());
        Assert.Equal(AppointmentService.ToUtc(stale.AppointmentDate, stale.StartTime), moved.StartAtUtc);
        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "AppointmentRescheduled");
    }

    [Fact]
    public async Task AppointmentSearchAndCalendarFilterByDate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var appointment = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-search"), Guid.CreateVersion7());

        var search = await service.SearchAsync(new AppointmentSearchViewModel { Search = appointment.AppointmentNumber, Date = date }, null);
        Assert.Single(search.Appointments);
        Assert.Equal(appointment.Id, search.Appointments.Single().Id);

        var calendar = await service.GetCalendarAsync(date, "day", null, null);
        Assert.Contains(calendar.Appointments, item => item.Id == appointment.Id);
    }

    [Fact]
    public async Task AppointmentNumberGeneratorReturnsDistinctNumbers()
    {
        var generator = new TestAppointmentNumberGenerator();
        Assert.NotEqual(await generator.GenerateAsync(), await generator.GenerateAsync());
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Receptionist)]
    public async Task StaffMustUseValidatedCancellationEndpoint(string role)
    {
        using var client = CreateMvcClient();
        var actor = await SignInAsync(client, role);
        var booked = await CreateMvcAppointmentAsync();
        var detailsUrl = $"/Appointments/Details/{booked.Id}";
        var token = await GetAntiforgeryTokenAsync(client, detailsUrl);
        var cancelUrl = $"/Appointments/Cancel/{booked.Id}";

        using var noToken = await PostAppointmentAsync(client, cancelUrl, booked, "Reason", "Requested by patient");
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        using var bypass = await PostAppointmentAsync(client, $"/Appointments/ChangeStatus/{booked.Id}",
            booked, "Status", nameof(AppointmentStatus.Cancelled), token);
        AssertDenied(bypass);

        using var invalidTransition = await PostAppointmentAsync(client, $"/Appointments/ChangeStatus/{booked.Id}",
            booked, "Status", nameof(AppointmentStatus.Completed), token);
        AssertDenied(invalidTransition);

        using var blankReason = await PostAppointmentAsync(client, cancelUrl, booked, "Reason", "   ", token);
        Assert.Equal(HttpStatusCode.Redirect, blankReason.StatusCode);
        var unchanged = await ReadAppointmentAsync(booked.Id);
        Assert.Equal(AppointmentStatus.Scheduled, unchanged.Status);
        Assert.Null(unchanged.CancelledAtUtc);
        Assert.Null(unchanged.CancelledByUserId);

        using var valid = await PostAppointmentAsync(client, cancelUrl, booked, "Reason", "Requested by patient", token);
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal(detailsUrl, valid.Headers.Location?.OriginalString);
        var cancelled = await ReadAppointmentAsync(booked.Id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Equal("Requested by patient", cancelled.CancellationReason);
        Assert.Equal(actor, cancelled.CancelledByUserId);
        Assert.NotNull(cancelled.CancelledAtUtc);
    }

    [Theory]
    [InlineData(HospitalRoles.Doctor)]
    [InlineData(HospitalRoles.Nurse)]
    public async Task UnauthorizedRolesCannotCancelAppointments(string role)
    {
        using var client = CreateMvcClient();
        var actor = await SignInAsync(client, role);
        var booked = await CreateMvcAppointmentAsync(role == HospitalRoles.Doctor ? actor : null);
        var token = await GetAntiforgeryTokenAsync(client, "/Account/ChangePassword");

        using var response = await PostAppointmentAsync(client, $"/Appointments/Cancel/{booked.Id}",
            booked, "Reason", "Requested by patient", token);

        AssertDenied(response);
        Assert.Equal(AppointmentStatus.Scheduled, (await ReadAppointmentAsync(booked.Id)).Status);
    }

    [Fact]
    public async Task DoctorCanOnlyViewAndChangeAppointmentsForTheirLinkedAccount()
    {
        using var client = CreateMvcClient();
        var actor = await SignInAsync(client, HospitalRoles.Doctor);
        var own = await CreateMvcAppointmentAsync(actor);
        using var otherClient = CreateMvcClient();
        var otherActor = await SignInAsync(otherClient, HospitalRoles.Doctor);
        var other = await CreateMvcAppointmentAsync(otherActor);

        using var unlinkedDetails = await client.GetAsync($"/Appointments/Details/{other.Id}");
        AssertDenied(unlinkedDetails);

        using var deniedSlots = await client.GetAsync(
            $"/Appointments/AvailableSlots?doctorId={other.DoctorId}&date={FutureWeekday(8):yyyy-MM-dd}&durationMinutes=30");
        AssertDenied(deniedSlots);
        using var deniedDoctors = await client.GetAsync($"/Appointments/EligibleDoctors?departmentId={other.DepartmentId}");
        AssertDenied(deniedDoctors);

        var token = await GetAntiforgeryTokenAsync(client, $"/Appointments/Details/{own.Id}");
        foreach (var url in new[] { "/Appointments" }.Concat(new[] { "day", "week", "month" }
            .Select(view => $"/Appointments/Calendar?view={view}&date={FutureWeekday(8):yyyy-MM-dd}")))
        {
            var html = await client.GetStringAsync(url);
            Assert.Contains(own.AppointmentNumber, html);
            Assert.DoesNotContain(other.AppointmentNumber, html);
        }

        using var denied = await PostAppointmentAsync(client, $"/Appointments/ChangeStatus/{other.Id}",
            other, "Status", nameof(AppointmentStatus.CheckedIn), token);
        AssertDenied(denied);

        using var allowed = await PostAppointmentAsync(client, $"/Appointments/ChangeStatus/{own.Id}",
            own, "Status", nameof(AppointmentStatus.CheckedIn), token);
        Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);
        Assert.Equal(AppointmentStatus.CheckedIn, (await ReadAppointmentAsync(own.Id)).Status);
        Assert.Equal(AppointmentStatus.Scheduled, (await ReadAppointmentAsync(other.Id)).Status);
    }

    [Fact]
    public async Task EligibleDoctorsEndpointOnlyReturnsActiveDoctorsInTheSelectedDepartment()
    {
        using var client = CreateMvcClient();
        await SignInAsync(client, HospitalRoles.Receptionist);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var first = await CreateFixtureAsync(db);
        var second = await CreateFixtureAsync(db);
        var inactive = await CreateFixtureAsync(db);
        var inactiveDoctor = await db.Doctors.SingleAsync(item => item.Id == inactive.DoctorId);
        inactiveDoctor.DepartmentId = first.DepartmentId;
        inactiveDoctor.IsActive = false;
        await db.SaveChangesAsync();

        var firstDoctors = await client.GetFromJsonAsync<DoctorOptionViewModel[]>($"/Appointments/EligibleDoctors?departmentId={first.DepartmentId}");
        var secondDoctors = await client.GetFromJsonAsync<DoctorOptionViewModel[]>($"/Appointments/EligibleDoctors?departmentId={second.DepartmentId}");
        Assert.Equal(first.DoctorId, Assert.Single(firstDoctors!).Id);
        Assert.Equal(second.DoctorId, Assert.Single(secondDoctors!).Id);

        var invalid = Booking(first, FutureWeekday(8), new TimeOnly(10, 0), Guid.NewGuid().ToString("N"));
        invalid.DoctorId = second.DoctorId;
        await Assert.ThrowsAsync<AppointmentValidationException>(() => CreateService(scope).BookAsync(invalid, Guid.NewGuid()));

        var doctorOptions = await client.GetStringAsync($"/Appointments/EligibleDoctors?departmentId={first.DepartmentId}");
        Assert.DoesNotContain("registrationNumber", doctorOptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phoneNumber", doctorOptions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BookingAndReschedulePagesRenderDoctorAndSlotControls()
    {
        using var client = CreateMvcClient();
        await SignInAsync(client, HospitalRoles.Receptionist);
        var booked = await CreateMvcAppointmentAsync();
        var create = await client.GetStringAsync("/Appointments/Create");
        Assert.Contains("data-doctors-url=\"/Appointments/EligibleDoctors\"", create);
        Assert.Contains("data-slots-url=\"/Appointments/AvailableSlots\"", create);
        Assert.Contains("/js/appointment-form.js", create);
        Assert.Contains("name=\"IdempotencyKey\"", create);

        var edit = await client.GetStringAsync($"/Appointments/Edit/{booked.Id}");
        Assert.Contains($"data-appointment-id=\"{booked.Id}\"", edit);
        Assert.Contains("name=\"ConcurrencyToken\"", edit);
        Assert.Contains("data-selected=\"10:00\"", edit);

        var url = $"/Appointments/AvailableSlots?doctorId={booked.DoctorId}&date={FutureWeekday(8):yyyy-MM-dd}&durationMinutes=30";
        var bookingSlots = await client.GetStringAsync(url);
        var editSlots = await client.GetStringAsync($"{url}&appointmentId={booked.Id}");
        Assert.DoesNotContain("10:00", bookingSlots);
        Assert.Contains("10:00", editSlots);
        Assert.DoesNotContain(booked.PatientId.ToString(), editSlots, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(booked.Id.ToString(), editSlots, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fictional", editSlots);
    }

    [Fact]
    public async Task DirectoryNextPagePreservesUpcomingAndOtherFilters()
    {
        using var client = CreateMvcClient();
        await SignInAsync(client, HospitalRoles.Administrator);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var first = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), Guid.NewGuid().ToString("N")), Guid.NewGuid());
        var second = await service.BookAsync(Booking(fixture, date, new TimeOnly(11, 0), Guid.NewGuid().ToString("N")), Guid.NewGuid());
        var url = $"/Appointments?UpcomingOnly=true&Search=APT-TEST&PageSize=1&DoctorId={fixture.DoctorId}" +
                  $"&DepartmentId={fixture.DepartmentId}&Date={date:yyyy-MM-dd}&Status=Scheduled&SortDirection=Desc";

        var page = await client.GetStringAsync(url);
        Assert.Contains(second.AppointmentNumber, page);
        Assert.DoesNotContain(first.AppointmentNumber, page);
        var next = Regex.Match(page, "href=\"([^\"]+)\"[^>]*>Next</a>");
        Assert.True(next.Success);
        var nextUrl = WebUtility.HtmlDecode(next.Groups[1].Value);
        foreach (var filter in new[] { "UpcomingOnly=True", "Search=APT-TEST", $"DoctorId={fixture.DoctorId}",
                     $"DepartmentId={fixture.DepartmentId}", $"Date={date:yyyy-MM-dd}", "Status=Scheduled", "SortDirection=Desc", "PageSize=1" })
            Assert.Contains(filter, nextUrl, StringComparison.OrdinalIgnoreCase);

        var nextPage = await client.GetStringAsync(nextUrl);
        Assert.Contains(first.AppointmentNumber, nextPage);
        Assert.DoesNotContain(second.AppointmentNumber, nextPage);
    }

    private HttpClient CreateMvcClient()
        => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private async Task<Guid> SignInAsync(HttpClient client, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);

        var email = $"appointments-{Guid.NewGuid():N}@example.test";
        const string password = "Test!Password123";
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(), UserName = email, Email = email,
            EmailConfirmed = true, IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);

        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password
        });
        using var response = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return user.Id;
    }

    private async Task<Appointment> CreateMvcAppointmentAsync(Guid? doctorAccountId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var doctor = await db.Doctors.SingleAsync(item => item.Id == fixture.DoctorId);
        doctor.ApplicationUserId = doctorAccountId;
        await db.SaveChangesAsync();
        return await CreateService(scope).BookAsync(
            Booking(fixture, FutureWeekday(8), new TimeOnly(10, 0), Guid.NewGuid().ToString("N")), Guid.CreateVersion7());
    }

    private async Task<Appointment> ReadAppointmentAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HospitalDbContext>()
            .Appointments.AsNoTracking().SingleAsync(item => item.Id == id);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Anti-forgery token missing from {url}.");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostAppointmentAsync(
        HttpClient client, string url, Appointment appointment, string field, string value, string? token = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["ConcurrencyToken"] = appointment.ConcurrencyToken.ToString(),
            [field] = value
        };
        if (token is not null)
            fields["__RequestVerificationToken"] = token;
        using var form = new FormUrlEncodedContent(fields);
        return await client.PostAsync(url, form);
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.StartsWith("/Account/AccessDenied", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }

    private IAppointmentService CreateService(IServiceScope scope)
        => new AppointmentService(
            scope.ServiceProvider.GetRequiredService<HospitalDbContext>(),
            new TestAppointmentNumberGenerator(),
            scope.ServiceProvider.GetRequiredService<IAuditLogService>(),
            Options.Create(new AppointmentSchedulingOptions()));

    private static AppointmentCreateViewModel Booking(AppointmentFixture fixture, DateOnly date, TimeOnly time, string key, int duration = 30) => new()
    {
        PatientId = fixture.PatientId,
        DepartmentId = fixture.DepartmentId,
        DoctorId = fixture.DoctorId,
        AppointmentDate = date,
        StartTime = time,
        DurationMinutes = duration,
        IdempotencyKey = key
    };

    private static async Task<AppointmentFixture> CreateFixtureAsync(HospitalDbContext db)
    {
        var department = new Department
        {
            Id = Guid.CreateVersion7(), DepartmentCode = $"D{Guid.NewGuid():N}"[..10],
            Name = $"Test Department {Guid.NewGuid():N}", IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        var patient = new Patient
        {
            Id = Guid.CreateVersion7(), PatientNumber = $"P{Guid.NewGuid():N}"[..20],
            FirstName = "Fictional", LastName = "Patient", DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Unknown", IsActive = true, ConcurrencyToken = Guid.CreateVersion7(),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        var doctor = new Doctor
        {
            Id = Guid.CreateVersion7(), DoctorNumber = $"D{Guid.NewGuid():N}"[..20],
            FirstName = "Fictional", LastName = "Doctor", RegistrationNumber = $"R{Guid.NewGuid():N}"[..20],
            Specialization = "General Medicine", DepartmentId = department.Id, Department = department,
            IsActive = true, ConcurrencyToken = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Departments.Add(department);
        db.Patients.Add(patient);
        db.Doctors.Add(doctor);
        db.DoctorSchedules.Add(new DoctorSchedule
        {
            Id = Guid.CreateVersion7(), DoctorId = doctor.Id, Doctor = doctor, DayOfWeek = 1,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, ConcurrencyToken = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        return new AppointmentFixture(patient.Id, doctor.Id, department.Id);
    }

    private static DateOnly FutureWeekday(int daysAhead)
    {
        var date = DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow)).AddDays(daysAhead);
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    private sealed record AppointmentFixture(Guid PatientId, Guid DoctorId, Guid DepartmentId);

    private sealed class TestAppointmentNumberGenerator : IAppointmentNumberGenerator
    {
        private static long _number;
        public Task<string> GenerateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult($"APT-TEST-{Interlocked.Increment(ref _number):D8}");
    }
}
