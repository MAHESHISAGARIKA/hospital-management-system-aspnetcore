using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class PostgreSqlAppointmentIntegrationTests
{
    [PostgreSqlFact]
    public async Task DatabaseEnforcesBookingAndReschedulingConcurrency()
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
        var otherDoctorId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();

        await using (var database = new HospitalDbContext(options))
        {
            Assert.Contains(await database.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddAppointmentManagement", StringComparison.Ordinal));
            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        }

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
                foreach (var id in new[] { doctorId, otherDoctorId })
                {
                    seedContext.Doctors.Add(new Doctor
                    {
                        Id = id,
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
                    seedContext.DoctorSchedules.Add(new DoctorSchedule
                    {
                        Id = Guid.CreateVersion7(),
                        DoctorId = id,
                        DayOfWeek = 1,
                        StartTime = new TimeOnly(9, 0),
                        EndTime = new TimeOnly(17, 0),
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                        ConcurrencyToken = Guid.CreateVersion7()
                    });
                }
                await seedContext.SaveChangesAsync();
            }

            var date = DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow)).AddDays(14);
            while (date.DayOfWeek != DayOfWeek.Monday)
                date = date.AddDays(1);

            async Task<Appointment> TryBookAsync(string key, TimeOnly? start = null, Guid? doctor = null, IAuditLogService? audit = null)
            {
                await using var context = new HospitalDbContext(options);
                var service = new AppointmentService(context, new AppointmentNumberGenerator(context), audit ?? new NoOpAuditLogService(), Options.Create(new AppointmentSchedulingOptions()));
                return await service.BookAsync(new AppointmentCreateViewModel
                {
                    PatientId = patientId,
                    DepartmentId = departmentId,
                    DoctorId = doctor ?? doctorId,
                    AppointmentDate = date,
                    StartTime = start ?? new TimeOnly(10, 0),
                    DurationMinutes = 30,
                    IdempotencyKey = key
                }, actorId);
            }

            var bookingResults = await Task.WhenAll(
                CaptureBookingAsync(() => TryBookAsync($"pg-{Guid.NewGuid():N}")),
                CaptureBookingAsync(() => TryBookAsync($"pg-{Guid.NewGuid():N}")));
            var successful = bookingResults.Where(result => result.Appointment is not null).Select(result => result.Appointment!).ToArray();
            Assert.Single(successful);
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

            verifyContext.ChangeTracker.Clear();
            var adjacent = await TryBookAsync($"pg-{Guid.NewGuid():N}", new TimeOnly(10, 30));
            Assert.Equal(successful[0].EndAtUtc, adjacent.StartAtUtc);
            Assert.NotEqual(successful[0].AppointmentNumber, adjacent.AppointmentNumber);

            var retryKey = $"pg-{Guid.NewGuid():N}";
            var retries = await Task.WhenAll(
                TryBookAsync(retryKey, new TimeOnly(11, 0)),
                TryBookAsync(retryKey, new TimeOnly(11, 0)));
            Assert.Equal(retries[0].Id, retries[1].Id);
            Assert.Equal(1, await verifyContext.Appointments.CountAsync(item =>
                item.CreatedByUserId == actorId && item.IdempotencyKey == retryKey));

            overlappingDirectInsert.StartAtUtc = successful[0].StartAtUtc.AddHours(4);
            overlappingDirectInsert.EndAtUtc = successful[0].EndAtUtc.AddHours(4);
            overlappingDirectInsert.IdempotencyKey = retryKey;
            verifyContext.Appointments.Add(overlappingDirectInsert);
            var duplicateKey = await Assert.ThrowsAsync<DbUpdateException>(() => verifyContext.SaveChangesAsync());
            Assert.Equal("ix_appointments_actor_idempotency_key", FindPostgresException(duplicateKey)?.ConstraintName);
            verifyContext.ChangeTracker.Clear();

            var onOtherDoctor = await TryBookAsync($"pg-{Guid.NewGuid():N}", new TimeOnly(11, 0), otherDoctorId);
            async Task<Appointment> MoveAsync(Appointment appointment, Guid targetDoctor)
            {
                await using var context = new HospitalDbContext(options);
                var service = new AppointmentService(context, new AppointmentNumberGenerator(context),
                    new NoOpAuditLogService(), Options.Create(new AppointmentSchedulingOptions()));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                return await service.RescheduleAsync(appointment.Id, new AppointmentRescheduleViewModel
                {
                    DoctorId = targetDoctor, DepartmentId = departmentId, AppointmentDate = date,
                    StartTime = new TimeOnly(12, 0), DurationMinutes = 30, ConcurrencyToken = appointment.ConcurrencyToken
                }, actorId, timeout.Token);
            }

            var moved = await Task.WhenAll(MoveAsync(retries[0], otherDoctorId), MoveAsync(onOtherDoctor, doctorId));
            Assert.Equal(otherDoctorId, moved[0].DoctorId);
            Assert.Equal(doctorId, moved[1].DoctorId);

            var rollbackKey = $"pg-{Guid.NewGuid():N}";
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TryBookAsync(rollbackKey, new TimeOnly(15, 0), audit: new FailingAuditLogService()));
            Assert.False(await verifyContext.Appointments.AnyAsync(item =>
                item.CreatedByUserId == actorId && item.IdempotencyKey == rollbackKey));
            Assert.NotNull(await TryBookAsync(rollbackKey, new TimeOnly(15, 0)));

        }
        finally
        {
            await using var cleanup = new HospitalDbContext(options);
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM appointments WHERE \"DoctorId\" IN ({doctorId}, {otherDoctorId})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM doctor_schedules WHERE \"DoctorId\" IN ({doctorId}, {otherDoctorId})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM doctors WHERE \"Id\" IN ({doctorId}, {otherDoctorId})");
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

    private sealed class FailingAuditLogService : IAuditLogService
    {
        public Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, string? details, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated audit failure.");
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
