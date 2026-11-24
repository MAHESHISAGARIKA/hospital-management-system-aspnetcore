using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Doctors;

public sealed class DoctorScheduleService(
    HospitalDbContext dbContext,
    IAuditLogService auditLogService) : IDoctorScheduleService
{
    public async Task<IReadOnlyCollection<DoctorSchedule>> GetForDoctorAsync(Guid doctorId, CancellationToken cancellationToken = default)
    {
        return await dbContext.DoctorSchedules
            .AsNoTracking()
            .Where(schedule => schedule.DoctorId == doctorId)
            .OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<DoctorSchedule> AddAsync(DoctorScheduleViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var doctor = await dbContext.Doctors
            .FirstOrDefaultAsync(item => item.Id == model.DoctorId, cancellationToken)
            ?? throw new DoctorScheduleValidationException("The selected doctor does not exist.");

        if (!doctor.IsActive)
            throw new DoctorScheduleValidationException("Inactive doctors cannot receive new schedules.");

        await LockDoctorScheduleRowAsync(model.DoctorId, cancellationToken);

        if (await HasOverlapAsync(model.DoctorId, model.DayOfWeek, model.StartTime, model.EndTime, null, cancellationToken))
            throw new DoctorScheduleConflictException("This schedule overlaps another active session for the doctor.");

        var schedule = new DoctorSchedule
        {
            Id = Guid.CreateVersion7(),
            DoctorId = doctor.Id,
            DayOfWeek = model.DayOfWeek,
            StartTime = model.StartTime,
            EndTime = model.EndTime,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.CreateVersion7()
        };

        dbContext.DoctorSchedules.Add(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditLogService.WriteAsync("DoctorScheduleCreated", actorUserId, null,
            $"Schedule {schedule.Id:D} created for doctor {doctor.Id:D}.", cancellationToken);

        return schedule;
    }

    public async Task<DoctorSchedule> UpdateAsync(DoctorScheduleViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        Validate(model);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var schedule = await dbContext.DoctorSchedules
            .FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken)
            ?? throw new DoctorScheduleNotFoundException(model.Id);

        if (schedule.ConcurrencyToken != model.ConcurrencyToken)
            throw new DoctorScheduleConcurrencyException("The schedule changed after it was opened. Reload and try again.");

        await LockDoctorScheduleRowAsync(schedule.DoctorId, cancellationToken);

        if (await HasOverlapAsync(schedule.DoctorId, model.DayOfWeek, model.StartTime, model.EndTime, schedule.Id, cancellationToken))
            throw new DoctorScheduleConflictException("This schedule overlaps another active session for the doctor.");

        schedule.DayOfWeek = model.DayOfWeek;
        schedule.StartTime = model.StartTime;
        schedule.EndTime = model.EndTime;
        schedule.IsActive = model.IsActive;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        schedule.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DoctorScheduleConcurrencyException("The schedule changed after it was opened. Reload and try again.", exception);
        }

        await auditLogService.WriteAsync("DoctorScheduleUpdated", actorUserId, null,
            $"Schedule {schedule.Id:D} updated for doctor {schedule.DoctorId:D}.", cancellationToken);

        return schedule;
    }

    public async Task DeactivateAsync(Guid scheduleId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var schedule = await dbContext.DoctorSchedules
            .FirstOrDefaultAsync(item => item.Id == scheduleId, cancellationToken)
            ?? throw new DoctorScheduleNotFoundException(scheduleId);

        if (!schedule.IsActive)
            return;

        schedule.IsActive = false;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        schedule.ConcurrencyToken = Guid.CreateVersion7();

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync("DoctorScheduleDeactivated", actorUserId, null,
            $"Schedule {schedule.Id:D} deactivated for doctor {schedule.DoctorId:D}.", cancellationToken);
    }

    private async Task<bool> HasOverlapAsync(
        Guid doctorId,
        int dayOfWeek,
        TimeOnly start,
        TimeOnly end,
        Guid? excludedScheduleId,
        CancellationToken cancellationToken)
    {
        var schedules = dbContext.DoctorSchedules
            .AsNoTracking()
            .Where(schedule => schedule.DoctorId == doctorId &&
                               schedule.DayOfWeek == dayOfWeek &&
                               schedule.IsActive);

        if (excludedScheduleId.HasValue)
            schedules = schedules.Where(schedule => schedule.Id != excludedScheduleId.Value);

        return await schedules.AnyAsync(schedule =>
            start < schedule.EndTime && end > schedule.StartTime,
            cancellationToken);
    }

    private async Task LockDoctorScheduleRowAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
            return;

        await dbContext.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({doctorId.ToString()}, 0))) AS schedule_lock")
            .FirstAsync(cancellationToken);
    }

    private static void Validate(DoctorScheduleViewModel model)
    {
        if (model.DayOfWeek is < 1 or > 7)
            throw new DoctorScheduleValidationException("Day of week must be from 1 through 7.");

        if (model.EndTime <= model.StartTime)
            throw new DoctorScheduleValidationException("End time must be later than start time.");
    }
}

public sealed class DoctorScheduleValidationException(string message) : Exception(message);
public sealed class DoctorScheduleNotFoundException(Guid id) : Exception($"Schedule '{id}' was not found.");
public sealed class DoctorScheduleConflictException(string message) : Exception(message);
public sealed class DoctorScheduleConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
