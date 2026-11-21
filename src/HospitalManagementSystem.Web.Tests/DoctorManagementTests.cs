using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Doctors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class DoctorManagementTests : IClassFixture<AuthenticationTestApplication>
{
    private readonly AuthenticationTestApplication _factory;

    public DoctorManagementTests(AuthenticationTestApplication factory) => _factory = factory;

    [Fact]
    public async Task DoctorRegistrationCreatesUniqueNumberAndNormalizedRegistration()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);

        var department = await CreateDepartmentAsync(db, "CARD", "Cardiology");
        var model = ValidDoctor(department.Id);
        model.RegistrationNumber = " slmc 123-AB ";

        var doctor = await doctorService.RegisterAsync(model, Guid.CreateVersion7());

        Assert.StartsWith("DOC-", doctor.DoctorNumber);
        Assert.Equal("SLMC123AB", doctor.RegistrationNumber);
        Assert.True(doctor.IsActive);
    }

    [Fact]
    public async Task DuplicateRegistrationNumberIsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);
        var department = await CreateDepartmentAsync(db, "NEUR", "Neurology");

        var first = ValidDoctor(department.Id);
        first.RegistrationNumber = "SLMC-123";
        await doctorService.RegisterAsync(first, Guid.CreateVersion7());

        var duplicate = ValidDoctor(department.Id);
        duplicate.RegistrationNumber = "slmc123";

        await Assert.ThrowsAsync<DoctorDuplicateException>(() =>
            doctorService.RegisterAsync(duplicate, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task RequiredFieldsAndInactiveDepartmentAreRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);

        await Assert.ThrowsAsync<DoctorValidationException>(() =>
            doctorService.RegisterAsync(new DoctorEditViewModel
            {
                FirstName = "",
                LastName = "",
                RegistrationNumber = "",
                Specialization = "",
                DepartmentId = Guid.CreateVersion7()
            }, Guid.CreateVersion7()));

        var department = await CreateDepartmentAsync(db, "OLD", "Inactive department", isActive: false);
        await Assert.ThrowsAsync<DoctorValidationException>(() =>
            doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7()));
    }

    [Fact]
    public async Task DoctorSearchFiltersAndPaginates()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var department = await CreateDepartmentAsync(db, "GEN", "General");

        db.Doctors.AddRange(
            CreateDoctor("DOC-000001", "Alice", "Zulu", department.Id, "General Medicine", true),
            CreateDoctor("DOC-000002", "Ben", "Alpha", department.Id, "Cardiology", false));
        await db.SaveChangesAsync();

        var doctorService = CreateDoctorService(scope);
        var result = await doctorService.SearchAsync(new DoctorSearchViewModel
        {
            DepartmentId = department.Id,
            Page = 1,
            PageSize = 1,
            SortBy = "DoctorNumber",
            SortDirection = "Asc"
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Doctors);
    }

    [Fact]
    public async Task DepartmentAssignmentAndAuditAreRecorded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);
        var firstDepartment = await CreateDepartmentAsync(db, "ORTH", "Orthopedics");
        var secondDepartment = await CreateDepartmentAsync(db, "SURG", "Surgery");
        var doctor = await doctorService.RegisterAsync(ValidDoctor(firstDepartment.Id), Guid.CreateVersion7());

        await doctorService.AssignDepartmentAsync(doctor.Id, secondDepartment.Id, Guid.CreateVersion7());

        var updated = await db.Doctors.FindAsync(doctor.Id);
        Assert.Equal(secondDepartment.Id, updated!.DepartmentId);
        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "DoctorDepartmentAssigned");
    }

    [Fact]
    public async Task DeactivateAndReactivatePreserveDoctorRecord()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);
        var department = await CreateDepartmentAsync(db, "DERM", "Dermatology");
        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());

        await doctorService.DeactivateAsync(doctor.Id, Guid.CreateVersion7());
        Assert.False((await db.Doctors.FindAsync(doctor.Id))!.IsActive);

        await doctorService.ReactivateAsync(doctor.Id, Guid.CreateVersion7());
        Assert.True((await db.Doctors.FindAsync(doctor.Id))!.IsActive);
    }

    [Fact]
    public async Task UpdateDetectsConcurrencyConflict()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var doctorService = CreateDoctorService(scope);
        var department = await CreateDepartmentAsync(db, "ENT", "ENT");
        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());

        var stale = DoctorEditFrom(doctor);
        doctor.ConcurrencyToken = Guid.CreateVersion7();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DoctorConcurrencyException>(() =>
            doctorService.UpdateAsync(stale, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task ScheduleRejectsInvalidRangeAndOverlaps()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var department = await CreateDepartmentAsync(db, "PED", "Pediatrics");

        var doctorService = CreateDoctorService(scope);
        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());

        var scheduleService = new DoctorScheduleService(db, scope.ServiceProvider.GetRequiredService<IAuditLogService>());

        await Assert.ThrowsAsync<DoctorScheduleValidationException>(() => scheduleService.AddAsync(new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(11, 0)
        }, Guid.CreateVersion7()));

        await scheduleService.AddAsync(new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0)
        }, Guid.CreateVersion7());

        await Assert.ThrowsAsync<DoctorScheduleConflictException>(() => scheduleService.AddAsync(new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(13, 0)
        }, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task ScheduleSupportsMultipleSessionsAndDeactivation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var department = await CreateDepartmentAsync(db, "OPH", "Ophthalmology");
        var doctorService = CreateDoctorService(scope);
        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());
        var scheduleService = new DoctorScheduleService(db, scope.ServiceProvider.GetRequiredService<IAuditLogService>());

        var morning = await scheduleService.AddAsync(new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0)
        }, Guid.CreateVersion7());

        var afternoon = await scheduleService.AddAsync(new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DayOfWeek = 1,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(17, 0)
        }, Guid.CreateVersion7());

        await scheduleService.DeactivateAsync(morning.Id, Guid.CreateVersion7());

        Assert.False((await db.DoctorSchedules.FindAsync(morning.Id))!.IsActive);
        Assert.True((await db.DoctorSchedules.FindAsync(afternoon.Id))!.IsActive);
    }

    [Fact]
    public async Task AccountLinkRequiresDoctorRoleAndPreventsReuse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var doctorService = CreateDoctorService(scope);
        var department = await CreateDepartmentAsync(db, "PHY", "Physiology");

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(HospitalRoles.Doctor))
            await roleManager.CreateAsync(new IdentityRole<Guid>(HospitalRoles.Doctor));

        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());
        var account = new ApplicationUser
        {
            Id = Guid.CreateVersion7(), UserName = $"doctor-{Guid.NewGuid():N}@example.test",
            Email = $"doctor-{Guid.NewGuid():N}@example.test", EmailConfirmed = true,
            IsEnabled = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            NormalizedEmail = "DOCTOR@EXAMPLE.TEST", NormalizedUserName = "DOCTOR@EXAMPLE.TEST"
        };
        await userManager.CreateAsync(account, "Valid!Password123");
        await userManager.AddToRoleAsync(account, HospitalRoles.Doctor);

        await doctorService.LinkAccountAsync(doctor.Id, account.Id, Guid.CreateVersion7());
        Assert.Equal(account.Id, (await db.Doctors.FindAsync(doctor.Id))!.ApplicationUserId);

        var secondDoctor = await doctorService.RegisterAsync(ValidDoctor(department.Id, registration: "SLMC-999"), Guid.CreateVersion7());
        await Assert.ThrowsAsync<DoctorAccountLinkException>(() =>
            doctorService.LinkAccountAsync(secondDoctor.Id, account.Id, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task AuditLogsDoctorLifecycleAndScheduleActions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var department = await CreateDepartmentAsync(db, "AUD", "Audit Medicine");
        var doctorService = CreateDoctorService(scope);
        var doctor = await doctorService.RegisterAsync(ValidDoctor(department.Id), Guid.CreateVersion7());

        await doctorService.DeactivateAsync(doctor.Id, Guid.CreateVersion7());

        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "DoctorCreated");
        Assert.Contains(await db.AuditLogEntries.ToArrayAsync(), entry => entry.Action == "DoctorDeactivated");
    }

    private IDoctorService CreateDoctorService(IServiceScope scope)
        => new DoctorService(scope.ServiceProvider.GetRequiredService<HospitalDbContext>(),
            scope.ServiceProvider.GetRequiredService<IAuditLogService>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            new TestDoctorNumberGenerator());
    private static DoctorEditViewModel ValidDoctor(Guid departmentId, string? registration = null) => new()
    {
        FirstName = "Test",
        LastName = "Doctor",
        RegistrationNumber = registration ?? $"SLMC-{Guid.NewGuid():N}",
        Specialization = "General Medicine",
        DepartmentId = departmentId,
        IsActive = true
    };

    private static DoctorEditViewModel DoctorEditFrom(Doctor doctor) => new()
    {
        Id = doctor.Id,
        DoctorNumber = doctor.DoctorNumber,
        FirstName = doctor.FirstName,
        LastName = doctor.LastName,
        RegistrationNumber = doctor.RegistrationNumber,
        Specialization = doctor.Specialization,
        Qualifications = doctor.Qualifications,
        PhoneNumber = doctor.PhoneNumber,
        Email = doctor.Email,
        DepartmentId = doctor.DepartmentId,
        ApplicationUserId = doctor.ApplicationUserId,
        IsActive = doctor.IsActive,
        ConcurrencyToken = doctor.ConcurrencyToken
    };

    private static Doctor CreateDoctor(string number, string firstName, string lastName, Guid departmentId, string specialization, bool active) => new()
    {
        Id = Guid.CreateVersion7(),
        DoctorNumber = number,
        FirstName = firstName,
        LastName = lastName,
        RegistrationNumber = $"SLMC-{Guid.NewGuid():N}",
        Specialization = specialization,
        DepartmentId = departmentId,
        IsActive = active,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        ConcurrencyToken = Guid.CreateVersion7()
    };

    private static async Task<Department> CreateDepartmentAsync(HospitalDbContext db, string code, string name, bool isActive = true)
    {
        var department = new Department
        {
            Id = Guid.CreateVersion7(),
            DepartmentCode = code,
            Name = name,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync();
        return department;
    }

    private sealed class TestDoctorNumberGenerator : IDoctorNumberGenerator
    {
        private long _sequence;
        public Task<string> GenerateAsync(CancellationToken cancellationToken = default)
        {
            var next = Interlocked.Increment(ref _sequence);
            return Task.FromResult($"DOC-{DateTime.UtcNow.Year}-{next:D6}");
        }
    }
}
