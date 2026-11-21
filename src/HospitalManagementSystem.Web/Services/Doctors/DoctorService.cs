using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Doctors;

public sealed class DoctorService(
    HospitalDbContext dbContext,
    IAuditLogService auditLogService,
    UserManager<ApplicationUser> userManager,
    IDoctorNumberGenerator doctorNumberGenerator) : IDoctorService
{
    public async Task<Doctor> RegisterAsync(DoctorEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Id == model.DepartmentId, cancellationToken)
            ?? throw new DoctorValidationException("The selected department does not exist.");

        if (!department.IsActive)
            throw new DoctorValidationException("The selected department is inactive.");

        var normalizedRegistration = NormalizeRegistrationNumber(model.RegistrationNumber);
        if (await dbContext.Doctors.AnyAsync(doctor => doctor.RegistrationNumber == normalizedRegistration, cancellationToken))
            throw new DoctorDuplicateException("A doctor with this professional registration number already exists.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var doctorNumber = await doctorNumberGenerator.GenerateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var doctor = new Doctor
        {
            Id = Guid.CreateVersion7(),
            DoctorNumber = doctorNumber,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            RegistrationNumber = normalizedRegistration,
            Specialization = model.Specialization.Trim(),
            Qualifications = NormalizeOptional(model.Qualifications),
            PhoneNumber = NormalizeOptional(model.PhoneNumber),
            Email = NormalizeOptional(model.Email)?.ToLowerInvariant(),
            DepartmentId = department.Id,
            ApplicationUserId = model.ApplicationUserId,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            UpdatedByUserId = actorUserId,
            ConcurrencyToken = Guid.CreateVersion7()
        };

        if (model.ApplicationUserId.HasValue)
        {
            await ValidateEligibleAccountAsync(model.ApplicationUserId.Value, cancellationToken);
            doctor.ApplicationUserId = model.ApplicationUserId;
        }

        dbContext.Doctors.Add(doctor);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DoctorPersistenceException("The doctor could not be registered.", exception);
        }

        await auditLogService.WriteAsync("DoctorCreated", actorUserId, null,
            $"Doctor {doctor.Id:D} created in department {department.Id:D}.", cancellationToken);

        return doctor;
    }

    public Task<Doctor?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Doctors.Include(doctor => doctor.Department)
            .AsNoTracking()
            .FirstOrDefaultAsync(doctor => doctor.Id == id, cancellationToken);

    public async Task<DoctorSearchViewModel> SearchAsync(DoctorSearchViewModel criteria, CancellationToken cancellationToken = default)
    {
        criteria.Page = Math.Max(criteria.Page, 1);
        criteria.PageSize = Math.Clamp(criteria.PageSize, 1, 100);

        var query = dbContext.Doctors.AsNoTracking()
            .Include(doctor => doctor.Department)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(doctor =>
                doctor.FirstName.Contains(search) || doctor.LastName.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(criteria.DoctorNumber))
            query = query.Where(doctor => doctor.DoctorNumber.Contains(criteria.DoctorNumber.Trim()));

        if (!string.IsNullOrWhiteSpace(criteria.RegistrationNumber))
        {
            var registration = NormalizeRegistrationNumber(criteria.RegistrationNumber);
            query = query.Where(doctor => doctor.RegistrationNumber.Contains(registration));
        }

        if (criteria.DepartmentId.HasValue)
            query = query.Where(doctor => doctor.DepartmentId == criteria.DepartmentId.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Specialization))
            query = query.Where(doctor => doctor.Specialization.Contains(criteria.Specialization.Trim()));

        if (criteria.IsActive.HasValue)
            query = query.Where(doctor => doctor.IsActive == criteria.IsActive.Value);

        var count = await query.CountAsync(cancellationToken);

        query = criteria.SortBy switch
        {
            "DoctorNumber" => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(doctor => doctor.DoctorNumber)
                : query.OrderBy(doctor => doctor.DoctorNumber),
            "Specialization" => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(doctor => doctor.Specialization)
                : query.OrderBy(doctor => doctor.Specialization),
            _ => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(doctor => doctor.LastName).ThenByDescending(doctor => doctor.FirstName)
                : query.OrderBy(doctor => doctor.LastName).ThenBy(doctor => doctor.FirstName)
        };

        criteria.Departments = await dbContext.Departments
            .AsNoTracking()
            .Where(department => department.IsActive)
            .OrderBy(department => department.Name)
            .ToArrayAsync(cancellationToken);

        criteria.TotalCount = count;
        criteria.Doctors = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToArrayAsync(cancellationToken);

        return criteria;
    }

    public async Task<Doctor> UpdateAsync(DoctorEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken)
            ?? throw new DoctorNotFoundException(model.Id);

        if (doctor.ConcurrencyToken != model.ConcurrencyToken)
            throw new DoctorConcurrencyException("The doctor record changed after it was opened. Reload and try again.");

        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Id == model.DepartmentId, cancellationToken)
            ?? throw new DoctorValidationException("The selected department does not exist.");

        if (!department.IsActive)
            throw new DoctorValidationException("The selected department is inactive.");

        var normalizedRegistration = NormalizeRegistrationNumber(model.RegistrationNumber);
        if (await dbContext.Doctors.AnyAsync(item =>
            item.Id != doctor.Id && item.RegistrationNumber == normalizedRegistration, cancellationToken))
        {
            throw new DoctorDuplicateException("A doctor with this professional registration number already exists.");
        }

        doctor.FirstName = model.FirstName.Trim();
        doctor.LastName = model.LastName.Trim();
        doctor.RegistrationNumber = normalizedRegistration;
        doctor.Specialization = model.Specialization.Trim();
        doctor.Qualifications = NormalizeOptional(model.Qualifications);
        doctor.PhoneNumber = NormalizeOptional(model.PhoneNumber);
        doctor.Email = NormalizeOptional(model.Email)?.ToLowerInvariant();
        doctor.DepartmentId = department.Id;
        doctor.IsActive = model.IsActive;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DoctorConcurrencyException("The doctor record changed after it was opened. Reload and try again.", exception);
        }

        await auditLogService.WriteAsync("DoctorUpdated", actorUserId, null,
            $"Doctor {doctor.Id:D} updated.", cancellationToken);

        return doctor;
    }

    public async Task AssignDepartmentAsync(Guid doctorId, Guid departmentId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == doctorId, cancellationToken)
            ?? throw new DoctorNotFoundException(doctorId);
        var department = await dbContext.Departments.FirstOrDefaultAsync(item => item.Id == departmentId, cancellationToken)
            ?? throw new DoctorValidationException("The selected department does not exist.");

        if (!department.IsActive)
            throw new DoctorValidationException("The selected department is inactive.");

        if (doctor.DepartmentId == departmentId)
            return;

        var oldDepartmentId = doctor.DepartmentId;
        doctor.DepartmentId = departmentId;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditLogService.WriteAsync("DoctorDepartmentAssigned", actorUserId, null,
            $"Doctor {doctor.Id:D} moved from department {oldDepartmentId:D} to department {departmentId:D}.", cancellationToken);
    }

    public async Task DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new DoctorNotFoundException(id);

        if (!doctor.IsActive)
            return;

        doctor.IsActive = false;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("DoctorDeactivated", actorUserId, null,
            $"Doctor {doctor.Id:D} deactivated.", cancellationToken);
    }

    public async Task ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new DoctorNotFoundException(id);

        if (doctor.IsActive)
            return;

        doctor.IsActive = true;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("DoctorReactivated", actorUserId, null,
            $"Doctor {doctor.Id:D} reactivated.", cancellationToken);
    }

    public async Task LinkAccountAsync(Guid doctorId, Guid applicationUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == doctorId, cancellationToken)
            ?? throw new DoctorNotFoundException(doctorId);

        if (doctor.ApplicationUserId == applicationUserId)
            return;

        await ValidateEligibleAccountAsync(applicationUserId, cancellationToken);

        if (await dbContext.Doctors.AnyAsync(item =>
            item.Id != doctorId && item.ApplicationUserId == applicationUserId, cancellationToken))
        {
            throw new DoctorAccountLinkException("This Identity account is already linked to another doctor.");
        }

        doctor.ApplicationUserId = applicationUserId;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditLogService.WriteAsync("DoctorAccountLinked", actorUserId, null,
            $"Doctor {doctor.Id:D} linked to account {applicationUserId:D}.", cancellationToken);
    }

    public async Task UnlinkAccountAsync(Guid doctorId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == doctorId, cancellationToken)
            ?? throw new DoctorNotFoundException(doctorId);

        if (!doctor.ApplicationUserId.HasValue)
            return;

        var oldAccountId = doctor.ApplicationUserId.Value;
        doctor.ApplicationUserId = null;
        doctor.UpdatedByUserId = actorUserId;
        doctor.UpdatedAtUtc = DateTime.UtcNow;
        doctor.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("DoctorAccountUnlinked", actorUserId, null,
            $"Doctor {doctor.Id:D} unlinked from account {oldAccountId:D}.", cancellationToken);
    }

    public async Task<IReadOnlyCollection<Doctor>> FindPossibleDuplicatesAsync(DoctorEditViewModel model, CancellationToken cancellationToken = default)
    {
        var registration = NormalizeRegistrationNumber(model.RegistrationNumber);
        return await dbContext.Doctors.AsNoTracking()
            .Where(doctor => doctor.RegistrationNumber == registration)
            .Take(10)
            .ToArrayAsync(cancellationToken);
    }

    private async Task ValidateEligibleAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await userManager.FindByIdAsync(accountId.ToString())
            ?? throw new DoctorAccountLinkException("The selected Identity account does not exist.");

        if (!account.IsEnabled)
            throw new DoctorAccountLinkException("The selected Identity account is disabled.");

        if (!await userManager.IsInRoleAsync(account, HospitalRoles.Doctor))
            throw new DoctorAccountLinkException("The selected account does not have the Doctor role.");
    }

    private static void Validate(DoctorEditViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FirstName) || string.IsNullOrWhiteSpace(model.LastName))
            throw new DoctorValidationException("First name and last name are required.");

        if (string.IsNullOrWhiteSpace(model.RegistrationNumber))
            throw new DoctorValidationException("Professional registration number is required.");

        if (string.IsNullOrWhiteSpace(model.Specialization))
            throw new DoctorValidationException("Specialization is required.");

        if (model.DepartmentId == Guid.Empty)
            throw new DoctorValidationException("A department is required.");
    }

    private static string NormalizeRegistrationNumber(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DoctorValidationException(string message) : Exception(message);
public sealed class DoctorNotFoundException(Guid id) : Exception($"Doctor '{id}' was not found.");
public sealed class DoctorConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class DoctorPersistenceException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class DoctorDuplicateException(string message) : Exception(message);
public sealed class DoctorAccountLinkException(string message) : Exception(message);
