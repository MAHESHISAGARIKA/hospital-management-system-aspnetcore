using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Sdk;

namespace HospitalManagementSystem.Web.Tests;

public sealed class PostgreSqlAppointmentIntegrationTests
{
    [PostgreSqlFact]
    public async Task DatabaseExclusionConstraintRejectsOverlappingDoctorRanges()
    {
        var connectionString = Environment.GetEnvironmentVariable("HOSPITAL_APPOINTMENT_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The PostgreSQL appointment test connection was not configured.");

        var connectionInfo = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.Equals(connectionInfo.Database, "hospital_management_dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Integration tests refuse to connect to hospital_management_dev.");
        if (string.IsNullOrWhiteSpace(connectionInfo.Database) || !connectionInfo.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The appointment integration database name must clearly identify a test database.");

        var options = new DbContextOptionsBuilder<HospitalDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var departmentId = Guid.CreateVersion7();
        var patientId = Guid.CreateVersion7();
        var doctorId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var appointmentIds = new List<Guid>();

        try
        {
            await using (var seedContext = new HospitalDbContext(options))
            {
                var department = new Department
                {
                    Id = departmentId,
                    DepartmentCode = $"T{Guid.NewGuid():N}"[..10],
                    Name = $"Integration {Guid.NewGuid():N}",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                seedContext.Departments.Add(department);
                seedContext.Patients.Add(new Patient
                {
                    Id = patientId,
                    PatientNumber = $"T{Guid.NewGuid():N}"[..20],
                    FirstName = "Fictional",
                    LastName = "Integration",
                    DateOfBirth = new DateOnly(1990, 1, 1),
                    Gender = "Unknown",
                    IsActive = true,
                    ConcurrencyToken = Guid.CreateVersion7(),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                seedContext.Doctors.Add(new Doctor
                {
                    Id = doctorId,
                    DoctorNumber = $"T{Guid.NewGuid():N}"[..20],
                    FirstName = "Fictional",
                    LastName = "Doctor",
                    RegistrationNumber = $"T{Guid.NewGuid():N}"[..20],
                    Specialization = "Integration Test",
                    DepartmentId = departmentId,
                    IsActive = true,
                    ConcurrencyToken = Guid.CreateVersion7(),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await seedContext.SaveChangesAsync();
                seedContext.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.CreateVersion7(),
                    DoctorId = doctorId,
                    DayOfWeek = 1,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsActive = true,
                    ConcurrencyToken = Guid.CreateVersion7(),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await seedContext.SaveChangesAsync();
            }

            var date = DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow)).AddDays(14);
            while (date.DayOfWeek != DayOfWeek.Monday)
                date = date.AddDays(1);

            async Task<Appointment> TryBookAsync(string key)
            {
                await using var context = new HospitalDbContext(options);
                var service = new AppointmentService(context, new AppointmentNumberGenerator(context), new NoOpAuditLogService(), Options.Create(new AppointmentSchedulingOptions()));
                return await service.BookAsync(new AppointmentCreateViewModel
                {
                    PatientId = patientId,
                    DepartmentId = departmentId,
                    DoctorId = doctorId,
                    AppointmentDate = date,
                    StartTime = new TimeOnly(10, 0),
                    DurationMinutes = 30,
                    IdempotencyKey = key
                }, actorId);
            }

            var bookingResults = await Task.WhenAll(
                CaptureBookingAsync(() => TryBookAsync($"pg-{Guid.NewGuid():N}")),
                CaptureBookingAsync(() => TryBookAsync($"pg-{Guid.NewGuid():N}")));
            var successful = bookingResults.Where(result => result.Appointment is not null).Select(result => result.Appointment!).ToArray();
            Assert.Single(successful);
            appointmentIds.Add(successful[0].Id);
            Assert.Single(bookingResults, result => result.Conflict is not null);

            await using var verifyContext = new HospitalDbContext(options);
            var overlappingDirectInsert = new Appointment
            {
                Id = Guid.CreateVersion7(),
                AppointmentNumber = $"APT-TEST-{Guid.NewGuid():N}"[..30],
                PatientId = patientId,
                DoctorId = doctorId,
                DepartmentId = departmentId,
                StartAtUtc = successful[0].StartAtUtc.AddMinutes(15),
                EndAtUtc = successful[0].EndAtUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                IdempotencyKey = $"direct-{Guid.NewGuid():N}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId,
                UpdatedByUserId = actorId,
                ConcurrencyToken = Guid.CreateVersion7()
            };
            verifyContext.Appointments.Add(overlappingDirectInsert);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
            Assert.Equal("23P01", FindPostgresException(exception)?.SqlState);
        }
        finally
        {
            await using var cleanup = new HospitalDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM appointments WHERE \"DoctorId\" = {doctorId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM doctor_schedules WHERE \"DoctorId\" = {doctorId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM doctors WHERE \"Id\" = {doctorId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM patients WHERE \"Id\" = {patientId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM departments WHERE \"Id\" = {departmentId}");
        }
    }

    private static async Task<(Appointment? Appointment, AppointmentConflictException? Conflict)> CaptureBookingAsync(Func<Task<Appointment>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (AppointmentConflictException conflict)
        {
            return (null, conflict);
        }
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException postgres)
                return postgres;
        return null;
    }

    private sealed class NoOpAuditLogService : IAuditLogService
    {
        public Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, string? details, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOSPITAL_APPOINTMENT_TEST_CONNECTION")))
            Skip = "Set HOSPITAL_APPOINTMENT_TEST_CONNECTION to a separate migrated PostgreSQL test database to run the exclusion/concurrency integration test.";
    }
}
