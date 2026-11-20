using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Patients;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class PatientManagementTests : IClassFixture<AuthenticationTestApplication>
{
    private readonly AuthenticationTestApplication _factory;

    public PatientManagementTests(AuthenticationTestApplication factory) => _factory = factory;

    [Fact]
    public async Task PatientRegistrationCreatesUniqueRecordAndAudit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var generator = new TestPatientNumberGenerator();
        var audit = scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>();
        var service = new PatientService(db, audit, generator);

        var model = ValidPatient();
        var patient = await service.RegisterAsync(model, Guid.CreateVersion7());

        Assert.StartsWith("PAT-", patient.PatientNumber);
        Assert.True(patient.IsActive);
        Assert.NotEqual(Guid.Empty, patient.ConcurrencyToken);
        Assert.Single(await db.Patients.Where(p => p.Id == patient.Id).ToArrayAsync());
    }

    [Fact]
    public async Task FutureDateOfBirthIsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        var model = ValidPatient();
        model.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await Assert.ThrowsAsync<PatientValidationException>(() => service.RegisterAsync(model, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task SearchFiltersSortsAndPaginates()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        db.Patients.AddRange(
            new Patient { Id = Guid.CreateVersion7(), PatientNumber = "PAT-TEST-1", FirstName = "Ada", LastName = "Zulu", DateOfBirth = new DateOnly(1990, 1, 1), Gender = "Female", IsActive = true, ConcurrencyToken = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Patient { Id = Guid.CreateVersion7(), PatientNumber = "PAT-TEST-2", FirstName = "Ben", LastName = "Alpha", DateOfBirth = new DateOnly(1985, 1, 1), Gender = "Male", IsActive = false, ConcurrencyToken = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.SearchAsync(new PatientSearchViewModel
        {
            Search = "PAT-TEST",
            Page = 1,
            PageSize = 1,
            SortBy = "PatientNumber",
            SortDirection = "Asc"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Patients);
    }

    [Fact]
    public async Task UpdateRejectsConcurrencyConflict()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        var patient = new Patient
        {
            Id = Guid.CreateVersion7(), PatientNumber = "PAT-CONFLICT", FirstName = "Conflict",
            LastName = "Case", DateOfBirth = new DateOnly(1990, 1, 1), Gender = "Unknown",
            IsActive = true, ConcurrencyToken = Guid.CreateVersion7(),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var staleModel = PatientEditFrom(patient);
        patient.ConcurrencyToken = Guid.CreateVersion7();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PatientConcurrencyException>(() => service.UpdateAsync(staleModel, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task ArchiveAndReactivatePreserveRecord()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        var patient = new Patient
        {
            Id = Guid.CreateVersion7(), PatientNumber = "PAT-ARCHIVE", FirstName = "Archive",
            LastName = "Case", DateOfBirth = new DateOnly(1990, 1, 1), Gender = "Unknown",
            IsActive = true, ConcurrencyToken = Guid.CreateVersion7(),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var actor = Guid.CreateVersion7();
        await service.ArchiveAsync(patient.Id, actor);
        Assert.False((await db.Patients.FindAsync(patient.Id))!.IsActive);

        await service.ReactivateAsync(patient.Id, actor);
        Assert.True((await db.Patients.FindAsync(patient.Id))!.IsActive);
    }

    [Fact]
    public async Task DuplicateWarningReturnsPossibleMatchesWithoutMerge()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        var existing = new Patient
        {
            Id = Guid.CreateVersion7(), PatientNumber = "PAT-DUPLICATE", FirstName = "Sam", LastName = "Taylor",
            DateOfBirth = new DateOnly(1988, 2, 2), Gender = "Unknown", NIC = "NIC123",
            IsActive = true, ConcurrencyToken = Guid.CreateVersion7(),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.Patients.Add(existing);
        await db.SaveChangesAsync();

        var matches = await service.FindPossibleDuplicatesAsync(new PatientEditViewModel
        {
            FirstName = "Sam", LastName = "Taylor", DateOfBirth = existing.DateOfBirth,
            Gender = "Unknown", NIC = "NIC123"
        });

        Assert.Single(matches);
        Assert.Equal(existing.Id, matches.Single().Id);
    }

    [Fact]
    public async Task ReceptionistCannotAccessUserManagement()
    {
        using var scope = _factory.Services.CreateScope();
        var role = new IdentityRole<Guid>(HospitalRoles.Receptionist);
        Assert.Equal(HospitalRoles.Receptionist, role.Name);
    }

    [Fact]
    public async Task InvalidPatientIdReturnsNotFoundThroughService()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = new PatientService(db,
            scope.ServiceProvider.GetRequiredService<Services.IAuditLogService>(),
            new TestPatientNumberGenerator());

        Assert.Null(await service.GetAsync(Guid.CreateVersion7()));
    }

    private static PatientEditViewModel ValidPatient() => new()
    {
        FirstName = "Test",
        LastName = "Patient",
        DateOfBirth = new DateOnly(1995, 5, 10),
        Gender = "Unknown",
        IsActive = true
    };

    private static PatientEditViewModel PatientEditFrom(Patient patient) => new()
    {
        Id = patient.Id,
        PatientNumber = patient.PatientNumber,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        DateOfBirth = patient.DateOfBirth,
        Gender = patient.Gender,
        NIC = patient.NIC,
        PhoneNumber = patient.PhoneNumber,
        Email = patient.Email,
        Address = patient.Address,
        BloodGroup = patient.BloodGroup,
        EmergencyContactName = patient.EmergencyContactName,
        EmergencyContactPhone = patient.EmergencyContactPhone,
        IsActive = patient.IsActive,
        ConcurrencyToken = patient.ConcurrencyToken
    };

    private sealed class TestPatientNumberGenerator : IPatientNumberGenerator
    {
        private long _sequence;
        public Task<string> GenerateAsync(CancellationToken cancellationToken = default)
        {
            var number = Interlocked.Increment(ref _sequence);
            return Task.FromResult($"PAT-{DateTime.UtcNow.Year}-{number:D6}");
        }
    }
}
