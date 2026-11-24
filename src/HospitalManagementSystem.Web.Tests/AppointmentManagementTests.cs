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
    public async Task CancellationRequiresReasonAndDoesNotDeleteAppointment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var fixture = await CreateFixtureAsync(db);
        var service = CreateService(scope);
        var date = FutureWeekday(8);
        var booked = await service.BookAsync(Booking(fixture, date, new TimeOnly(10, 0), "request-cancel"), Guid.CreateVersion7());

        await Assert.ThrowsAsync<AppointmentValidationException>(() => service.CancelAsync(booked.Id,
            new AppointmentCancellationViewModel { Reason = " " , ConcurrencyToken = booked.ConcurrencyToken }, Guid.CreateVersion7()));

        var actor = Guid.CreateVersion7();
        await service.CancelAsync(booked.Id,
            new AppointmentCancellationViewModel { Reason = "Patient requested cancellation", ConcurrencyToken = booked.ConcurrencyToken }, actor);

        var cancelled = await db.Appointments.SingleAsync(item => item.Id == booked.Id);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Equal(actor, cancelled.CancelledByUserId);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "AppointmentCancelled");

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
