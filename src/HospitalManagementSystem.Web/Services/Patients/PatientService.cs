using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Patients;

public sealed class PatientService(
    HospitalDbContext dbContext,
    IAuditLogService auditLogService,
    IPatientNumberGenerator patientNumberGenerator) : IPatientService
{
    public async Task<Patient> RegisterAsync(PatientEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var duplicates = await FindPossibleDuplicatesAsync(model, cancellationToken);
        if (duplicates.Count > 0)
        {
            // Duplicate matches are warnings for the UI; registration remains a deliberate operator action.
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var patientNumber = await patientNumberGenerator.GenerateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var patient = new Patient
        {
            Id = Guid.CreateVersion7(),
            PatientNumber = patientNumber,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender.Trim(),
            NIC = NormalizeOptional(model.NIC),
            PhoneNumber = NormalizeOptional(model.PhoneNumber),
            Email = NormalizeOptional(model.Email)?.ToLowerInvariant(),
            Address = NormalizeOptional(model.Address),
            BloodGroup = NormalizeOptional(model.BloodGroup)?.ToUpperInvariant(),
            EmergencyContactName = NormalizeOptional(model.EmergencyContactName),
            EmergencyContactPhone = NormalizeOptional(model.EmergencyContactPhone),
            CreatedByUserId = actorUserId,
            UpdatedByUserId = actorUserId,
            IsActive = true,
            ConcurrencyToken = Guid.CreateVersion7(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Patients.Add(patient);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (HasUniquePatientNumberViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PatientNumberConflictException("A patient number conflict occurred. Retry registration.", exception);
        }

        await auditLogService.WriteAsync("PatientCreated", actorUserId, null,
            $"Patient {patient.Id:D} created.", cancellationToken);

        return patient;
    }

    public Task<Patient?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(patient => patient.Id == id, cancellationToken);

    public async Task<PatientSearchViewModel> SearchAsync(PatientSearchViewModel criteria, CancellationToken cancellationToken = default)
    {
        criteria.Page = Math.Max(criteria.Page, 1);
        criteria.PageSize = Math.Clamp(criteria.PageSize, 1, 100);

        var query = dbContext.Patients.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(patient =>
                patient.PatientNumber.Contains(search) ||
                patient.FirstName.Contains(search) ||
                patient.LastName.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Gender))
            query = query.Where(patient => patient.Gender == criteria.Gender);

        if (criteria.IsActive.HasValue)
            query = query.Where(patient => patient.IsActive == criteria.IsActive.Value);

        var count = await query.CountAsync(cancellationToken);

        query = criteria.SortBy switch
        {
            "PatientNumber" => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(patient => patient.PatientNumber)
                : query.OrderBy(patient => patient.PatientNumber),
            "DateOfBirth" => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(patient => patient.DateOfBirth)
                : query.OrderBy(patient => patient.DateOfBirth),
            _ => criteria.SortDirection == "Desc"
                ? query.OrderByDescending(patient => patient.LastName).ThenByDescending(patient => patient.FirstName)
                : query.OrderBy(patient => patient.LastName).ThenBy(patient => patient.FirstName)
        };

        criteria.TotalCount = count;
        criteria.Patients = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToArrayAsync(cancellationToken);

        return criteria;
    }

    public async Task<Patient> UpdateAsync(PatientEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var patient = await dbContext.Patients.FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken)
            ?? throw new PatientNotFoundException(model.Id);

        if (patient.ConcurrencyToken != model.ConcurrencyToken)
            throw new PatientConcurrencyException("The patient record changed after it was opened. Reload and try again.");

        patient.FirstName = model.FirstName.Trim();
        patient.LastName = model.LastName.Trim();
        patient.DateOfBirth = model.DateOfBirth;
        patient.Gender = model.Gender.Trim();
        patient.NIC = NormalizeOptional(model.NIC);
        patient.PhoneNumber = NormalizeOptional(model.PhoneNumber);
        patient.Email = NormalizeOptional(model.Email)?.ToLowerInvariant();
        patient.Address = NormalizeOptional(model.Address);
        patient.BloodGroup = NormalizeOptional(model.BloodGroup)?.ToUpperInvariant();
        patient.EmergencyContactName = NormalizeOptional(model.EmergencyContactName);
        patient.EmergencyContactPhone = NormalizeOptional(model.EmergencyContactPhone);
        patient.IsActive = model.IsActive;
        patient.UpdatedByUserId = actorUserId;
        patient.UpdatedAtUtc = DateTime.UtcNow;
        patient.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PatientConcurrencyException("The patient record changed after it was opened. Reload and try again.", exception);
        }

        await auditLogService.WriteAsync("PatientUpdated", actorUserId, null,
            $"Patient {patient.Id:D} demographics updated.", cancellationToken);

        return patient;
    }

    public async Task ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var patient = await dbContext.Patients.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new PatientNotFoundException(id);

        if (!patient.IsActive)
            return;

        patient.IsActive = false;
        patient.UpdatedByUserId = actorUserId;
        patient.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("PatientArchived", actorUserId, null,
            $"Patient {patient.Id:D} archived.", cancellationToken);
    }

    public async Task ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var patient = await dbContext.Patients.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new PatientNotFoundException(id);

        if (patient.IsActive)
            return;

        patient.IsActive = true;
        patient.UpdatedByUserId = actorUserId;
        patient.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("PatientReactivated", actorUserId, null,
            $"Patient {patient.Id:D} reactivated.", cancellationToken);
    }

    public async Task<IReadOnlyCollection<Patient>> FindPossibleDuplicatesAsync(PatientEditViewModel model, CancellationToken cancellationToken = default)
    {
        var normalizedNic = NormalizeOptional(model.NIC);
        var firstName = model.FirstName.Trim();
        var lastName = model.LastName.Trim();

        IQueryable<Patient> query = dbContext.Patients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(normalizedNic))
        {
            var nicMatches = await query.Where(patient => patient.NIC == normalizedNic).ToArrayAsync(cancellationToken);
            if (nicMatches.Length > 0)
                return nicMatches;
        }

        return await query.Where(patient =>
            patient.FirstName.ToLower() == firstName.ToLower() &&
            patient.LastName.ToLower() == lastName.ToLower() &&
            patient.DateOfBirth == model.DateOfBirth)
            .Take(10)
            .ToArrayAsync(cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Validate(PatientEditViewModel model)
    {
        if (model.DateOfBirth == default || model.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new PatientValidationException("Date of birth is required and cannot be in the future.");

        if (string.IsNullOrWhiteSpace(model.FirstName) || string.IsNullOrWhiteSpace(model.LastName))
            throw new PatientValidationException("First name and last name are required.");

        if (string.IsNullOrWhiteSpace(model.Gender))
            throw new PatientValidationException("Gender is required.");
    }

    private static bool HasUniquePatientNumberViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("ix_patients_patient_number", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class PatientValidationException(string message) : Exception(message);
public sealed class PatientNotFoundException(Guid id) : Exception($"Patient '{id}' was not found.");
public sealed class PatientConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class PatientNumberConflictException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class PatientDuplicateWarningException(IReadOnlyCollection<Patient> duplicates) : Exception("Possible duplicate patient records were found.")
{
    public IReadOnlyCollection<Patient> Duplicates { get; } = duplicates;
}
