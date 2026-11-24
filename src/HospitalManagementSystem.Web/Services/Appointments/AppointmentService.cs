using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Appointments;

public sealed class AppointmentService(
    HospitalDbContext dbContext,
    IAppointmentNumberGenerator numberGenerator,
    IAuditLogService auditLogService,
    IOptions<AppointmentSchedulingOptions> schedulingOptions) : IAppointmentService
{
    private static readonly TimeZoneInfo ColomboTimeZone = ResolveColomboTimeZone();
    private static readonly AppointmentStatus[] BlockingStatuses = [AppointmentStatus.Scheduled, AppointmentStatus.CheckedIn];
    private readonly AppointmentSchedulingOptions _scheduling = schedulingOptions.Value;

    public async Task<Appointment> BookAsync(AppointmentCreateViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var idempotencyKey = NormalizeIdempotencyKey(model.IdempotencyKey);
        var reason = NormalizeOptional(model.ReasonForVisit);
        var notes = NormalizeOptional(model.Notes);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            model.PatientId,
            model.DoctorId,
            model.DepartmentId,
            Date = model.AppointmentDate.DayNumber,
            Start = model.StartTime.Ticks,
            model.DurationMinutes,
            Reason = reason,
            Notes = notes
        })));

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync($"appointment-request:{actorUserId:D}:{idempotencyKey}", cancellationToken);

        var existingRequest = await dbContext.Appointments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CreatedByUserId == actorUserId &&
                                         item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingRequest is not null)
        {
            if (existingRequest.BookingRequestFingerprint == fingerprint)
                return existingRequest;

            throw new AppointmentIdempotencyConflictException("This booking request key cannot be reused for this request.");
        }

        ValidateDurationAndStart(model.StartTime, model.DurationMinutes);
        await LockDoctorAsync(model.DoctorId, cancellationToken);

        var patient = await dbContext.Patients
            .FirstOrDefaultAsync(item => item.Id == model.PatientId, cancellationToken)
            ?? throw new AppointmentValidationException("The selected patient does not exist.");
        if (!patient.IsActive)
            throw new AppointmentValidationException("Archived patients cannot be booked.");

        var doctor = await dbContext.Doctors
            .FirstOrDefaultAsync(item => item.Id == model.DoctorId, cancellationToken)
            ?? throw new AppointmentValidationException("The selected doctor does not exist.");
        if (!doctor.IsActive)
            throw new AppointmentValidationException("Inactive doctors cannot be booked.");
        if (doctor.DepartmentId != model.DepartmentId)
            throw new AppointmentValidationException("The selected doctor does not belong to the selected department.");

        var department = await dbContext.Departments
            .FirstOrDefaultAsync(item => item.Id == model.DepartmentId, cancellationToken)
            ?? throw new AppointmentValidationException("The selected department does not exist.");
        if (!department.IsActive)
            throw new AppointmentValidationException("Inactive departments cannot be used for bookings.");

        var startAtUtc = ToUtc(model.AppointmentDate, model.StartTime);
        var endAtUtc = startAtUtc.AddMinutes(model.DurationMinutes);
        if (startAtUtc <= DateTime.UtcNow)
            throw new AppointmentValidationException("Appointments must be booked in the future.");

        await ValidateScheduleAsync(doctor.Id, model.AppointmentDate, model.StartTime,
            model.StartTime.AddMinutes(model.DurationMinutes), cancellationToken);
        await EnsureNoOverlapAsync(doctor.Id, startAtUtc, endAtUtc, null, cancellationToken);

        var now = DateTime.UtcNow;
        var appointment = new Appointment
        {
            Id = Guid.CreateVersion7(),
            AppointmentNumber = await numberGenerator.GenerateAsync(cancellationToken),
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DepartmentId = department.Id,
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            Status = AppointmentStatus.Scheduled,
            ReasonForVisit = reason,
            Notes = notes,
            BookingRequestFingerprint = fingerprint,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = actorUserId,
            UpdatedByUserId = actorUserId,
            ConcurrencyToken = Guid.CreateVersion7()
        };

        dbContext.Appointments.Add(appointment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditLogService.WriteAsync("AppointmentCreated", actorUserId, null,
                $"Appointment {appointment.Id:D} ({appointment.AppointmentNumber}) created.", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsExclusionViolation(exception))
        {
            throw new AppointmentConflictException("That time was just booked. Select another available slot.", exception);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new AppointmentIdempotencyConflictException("This booking was already submitted. Refresh the appointment list.", exception);
        }

        return appointment;
    }

    public Task<Appointment?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Appointments
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Department)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public async Task<AppointmentSearchViewModel> SearchAsync(
        AppointmentSearchViewModel criteria,
        Guid? doctorAccountId,
        CancellationToken cancellationToken = default)
    {
        criteria.Page = Math.Max(1, criteria.Page);
        criteria.PageSize = Math.Clamp(criteria.PageSize, 1, 100);

        var query = dbContext.Appointments.AsNoTracking()
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Department)
            .AsQueryable();

        if (doctorAccountId.HasValue)
            query = query.Where(item => item.Doctor.ApplicationUserId == doctorAccountId.Value);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim();
            query = query.Where(item => item.AppointmentNumber.Contains(term) ||
                                        item.Patient.PatientNumber.Contains(term) ||
                                        item.Patient.FirstName.Contains(term) ||
                                        item.Patient.LastName.Contains(term));
        }
        if (criteria.DoctorId.HasValue)
            query = query.Where(item => item.DoctorId == criteria.DoctorId.Value);
        if (criteria.DepartmentId.HasValue)
            query = query.Where(item => item.DepartmentId == criteria.DepartmentId.Value);
        if (criteria.Status.HasValue)
            query = query.Where(item => item.Status == criteria.Status.Value);
        if (criteria.Date.HasValue)
        {
            var localStart = ToUtc(criteria.Date.Value, TimeOnly.MinValue);
            var localEnd = ToUtc(criteria.Date.Value.AddDays(1), TimeOnly.MinValue);
            query = query.Where(item => item.StartAtUtc >= localStart && item.StartAtUtc < localEnd);
        }
        if (criteria.UpcomingOnly)
            query = query.Where(item => item.StartAtUtc >= DateTime.UtcNow && BlockingStatuses.Contains(item.Status));

        var total = await query.CountAsync(cancellationToken);
        criteria.Page = Math.Min(criteria.Page, Math.Max(1, (int)Math.Ceiling((double)total / criteria.PageSize)));
        query = criteria.SortDirection == "Desc"
            ? query.OrderByDescending(item => item.StartAtUtc).ThenBy(item => item.Id)
            : query.OrderBy(item => item.StartAtUtc).ThenBy(item => item.Id);

        criteria.TotalCount = total;
        var rows = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToArrayAsync(cancellationToken);
        criteria.Appointments = rows.Select(ToListItem).ToArray();

        criteria.Doctors = await dbContext.Doctors.AsNoTracking()
            .Where(item => item.IsActive && (!doctorAccountId.HasValue || item.ApplicationUserId == doctorAccountId.Value))
            .OrderBy(item => item.LastName).ThenBy(item => item.FirstName)
            .Select(item => new DoctorOptionViewModel { Id = item.Id, DisplayName = "Dr. " + item.FirstName + " " + item.LastName })
            .ToArrayAsync(cancellationToken);
        criteria.Departments = await dbContext.Departments.AsNoTracking()
            .Where(item => item.IsActive).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);

        return criteria;
    }

    public async Task<IReadOnlyCollection<TimeOnly>> GetAvailableSlotsAsync(
        Guid doctorId,
        DateOnly localDate,
        int durationMinutes,
        CancellationToken cancellationToken = default,
        Guid? excludingAppointmentId = null)
    {
        if (!_scheduling.AllowedDurationMinutes.Contains(durationMinutes))
            throw new AppointmentValidationException("Choose a supported appointment duration.");

        var doctor = await dbContext.Doctors.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == doctorId && item.IsActive &&
                dbContext.Departments.Any(department => department.Id == item.DepartmentId && department.IsActive), cancellationToken);
        if (doctor is null)
            return [];

        var localDay = GetScheduleDay(localDate);
        var schedules = await dbContext.DoctorSchedules.AsNoTracking()
            .Where(item => item.DoctorId == doctorId && item.DayOfWeek == localDay && item.IsActive)
            .OrderBy(item => item.StartTime)
            .ToArrayAsync(cancellationToken);

        var dayStartUtc = ToUtc(localDate, TimeOnly.MinValue);
        var dayEndUtc = ToUtc(localDate.AddDays(1), TimeOnly.MinValue);
        var busyAppointments = await dbContext.Appointments.AsNoTracking()
            .Where(item => item.DoctorId == doctorId && BlockingStatuses.Contains(item.Status) &&
                           item.Id != excludingAppointmentId && item.StartAtUtc < dayEndUtc && item.EndAtUtc > dayStartUtc)
            .Select(item => new { item.StartAtUtc, item.EndAtUtc })
            .ToArrayAsync(cancellationToken);

        var slots = new SortedSet<TimeOnly>();
        var nowUtc = DateTime.UtcNow;
        foreach (var schedule in schedules)
        {
            var interval = TimeSpan.FromMinutes(_scheduling.StartIntervalMinutes).Ticks;
            var firstTick = ((schedule.StartTime.Ticks + interval - 1) / interval) * interval;
            var durationTicks = TimeSpan.FromMinutes(durationMinutes).Ticks;
            for (var tick = firstTick; tick + durationTicks <= schedule.EndTime.Ticks; tick += interval)
            {
                var start = new TimeOnly(tick);
                var startUtc = ToUtc(localDate, start);
                var endUtc = startUtc.AddMinutes(durationMinutes);
                if (startUtc <= nowUtc)
                    continue;
                if (busyAppointments.Any(item => startUtc < item.EndAtUtc && endUtc > item.StartAtUtc))
                    continue;
                slots.Add(start);
            }
        }

        return slots.ToArray();
    }

    public async Task<Appointment> RescheduleAsync(
        Guid id,
        AppointmentRescheduleViewModel model,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateDurationAndStart(model.StartTime, model.DurationMinutes);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var appointment = await dbContext.Appointments
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new AppointmentNotFoundException(id);
        if (appointment.ConcurrencyToken != model.ConcurrencyToken)
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.");
        if (appointment.Status != AppointmentStatus.Scheduled)
            throw new AppointmentStatusException("Only scheduled appointments can be rescheduled.");

        foreach (var doctorId in new[] { appointment.DoctorId, model.DoctorId }.Distinct().Order())
            await LockDoctorAsync(doctorId, cancellationToken);

        var patient = await dbContext.Patients.FirstOrDefaultAsync(item => item.Id == appointment.PatientId, cancellationToken);
        if (patient is null || !patient.IsActive)
            throw new AppointmentValidationException("Archived or missing patients cannot be rescheduled.");

        var doctor = await dbContext.Doctors.FirstOrDefaultAsync(item => item.Id == model.DoctorId, cancellationToken)
            ?? throw new AppointmentValidationException("The selected doctor does not exist.");
        if (!doctor.IsActive || doctor.DepartmentId != model.DepartmentId)
            throw new AppointmentValidationException("Choose an active doctor in the selected department.");
        var department = await dbContext.Departments.FirstOrDefaultAsync(item => item.Id == model.DepartmentId, cancellationToken);
        if (department is null || !department.IsActive)
            throw new AppointmentValidationException("The selected department is inactive or missing.");

        var newStartUtc = ToUtc(model.AppointmentDate, model.StartTime);
        var newEndUtc = newStartUtc.AddMinutes(model.DurationMinutes);
        if (newStartUtc <= DateTime.UtcNow)
            throw new AppointmentValidationException("Appointments cannot be rescheduled into the past.");
        await ValidateScheduleAsync(model.DoctorId, model.AppointmentDate, model.StartTime,
            model.StartTime.AddMinutes(model.DurationMinutes), cancellationToken);
        await EnsureNoOverlapAsync(model.DoctorId, newStartUtc, newEndUtc, appointment.Id, cancellationToken);

        var oldStartUtc = appointment.StartAtUtc;
        var oldEndUtc = appointment.EndAtUtc;
        var oldDoctorId = appointment.DoctorId;
        appointment.DoctorId = doctor.Id;
        appointment.DepartmentId = department.Id;
        appointment.StartAtUtc = newStartUtc;
        appointment.EndAtUtc = newEndUtc;
        appointment.UpdatedAtUtc = DateTime.UtcNow;
        appointment.UpdatedByUserId = actorUserId;
        appointment.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditLogService.WriteAsync("AppointmentRescheduled", actorUserId, null,
                $"Appointment {appointment.Id:D} moved from {oldStartUtc:O}-{oldEndUtc:O} doctor {oldDoctorId:D} to {newStartUtc:O}-{newEndUtc:O} doctor {doctor.Id:D}.", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsExclusionViolation(exception))
        {
            throw new AppointmentConflictException("That time was just booked. Select another available slot.", exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.", exception);
        }

        return appointment;
    }

    public async Task CancelAsync(Guid id, AppointmentCancellationViewModel model, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var reason = model.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 300)
            throw new AppointmentValidationException("Enter a cancellation reason of up to 300 characters.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var appointment = await dbContext.Appointments.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new AppointmentNotFoundException(id);
        if (appointment.ConcurrencyToken != model.ConcurrencyToken)
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.");
        if (!IsValidTransition(appointment.Status, AppointmentStatus.Cancelled))
            throw new AppointmentStatusException("This appointment can no longer be cancelled.");

        var oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = reason;
        var cancelledAtUtc = DateTime.UtcNow;
        appointment.CancelledAtUtc = cancelledAtUtc;
        appointment.CancelledByUserId = actorUserId;
        appointment.UpdatedAtUtc = cancelledAtUtc;
        appointment.UpdatedByUserId = actorUserId;
        appointment.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.", exception);
        }

        await auditLogService.WriteAsync("AppointmentCancelled", actorUserId, null,
            $"Appointment {appointment.Id:D} changed from {oldStatus} to Cancelled.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Appointment> ChangeStatusAsync(
        Guid id,
        AppointmentStatusViewModel model,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var appointment = await dbContext.Appointments.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new AppointmentNotFoundException(id);
        if (appointment.ConcurrencyToken != model.ConcurrencyToken)
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.");

        if (model.Status == AppointmentStatus.Cancelled)
            throw new AppointmentStatusException("Use the cancellation workflow to cancel an appointment.");

        if (!IsValidTransition(appointment.Status, model.Status))
            throw new AppointmentStatusException($"The status cannot change from {appointment.Status} to {model.Status}.");

        var oldStatus = appointment.Status;
        appointment.Status = model.Status;
        appointment.UpdatedAtUtc = DateTime.UtcNow;
        appointment.UpdatedByUserId = actorUserId;
        appointment.ConcurrencyToken = Guid.CreateVersion7();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AppointmentConcurrencyException("This appointment changed. Reload and try again.", exception);
        }

        await auditLogService.WriteAsync("AppointmentStatusChanged", actorUserId, null,
            $"Appointment {appointment.Id:D} changed from {oldStatus} to {appointment.Status}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return appointment;
    }

    public async Task<AppointmentCalendarViewModel> GetCalendarAsync(
        DateOnly date,
        string view,
        Guid? doctorId,
        Guid? doctorAccountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedView = view is "day" or "month" ? view : "week";
        var range = GetLocalCalendarRange(date, normalizedView);
        var startUtc = ToUtc(range.Start, TimeOnly.MinValue);
        var endUtc = ToUtc(range.End, TimeOnly.MinValue);

        var query = dbContext.Appointments.AsNoTracking()
            .Include(item => item.Patient)
            .Include(item => item.Doctor)
            .Include(item => item.Department)
            .Where(item => item.StartAtUtc < endUtc && item.EndAtUtc > startUtc);

        if (doctorId.HasValue)
            query = query.Where(item => item.DoctorId == doctorId.Value);
        if (doctorAccountId.HasValue)
            query = query.Where(item => item.Doctor.ApplicationUserId == doctorAccountId.Value);

        var rows = await query.OrderBy(item => item.StartAtUtc).ToArrayAsync(cancellationToken);
        return new AppointmentCalendarViewModel
        {
            Date = date,
            View = normalizedView,
            DoctorId = doctorId,
            Doctors = await dbContext.Doctors.AsNoTracking()
                .Where(item => item.IsActive && (!doctorAccountId.HasValue || item.ApplicationUserId == doctorAccountId.Value))
                .OrderBy(item => item.LastName).ThenBy(item => item.FirstName)
                .Select(item => new DoctorOptionViewModel { Id = item.Id, DisplayName = "Dr. " + item.FirstName + " " + item.LastName })
                .ToArrayAsync(cancellationToken),
            Appointments = rows.Select(ToListItem).ToArray()
        };
    }

    public static bool IsValidTransition(AppointmentStatus current, AppointmentStatus next)
        => current switch
        {
            AppointmentStatus.Scheduled => next is AppointmentStatus.CheckedIn or AppointmentStatus.Cancelled or AppointmentStatus.Missed,
            AppointmentStatus.CheckedIn => next is AppointmentStatus.Completed or AppointmentStatus.Cancelled,
            _ => false
        };

    public static IReadOnlyCollection<AppointmentStatus> AllowedNextStatuses(AppointmentStatus current)
        => current switch
        {
            AppointmentStatus.Scheduled => [AppointmentStatus.CheckedIn, AppointmentStatus.Cancelled, AppointmentStatus.Missed],
            AppointmentStatus.CheckedIn => [AppointmentStatus.Completed, AppointmentStatus.Cancelled],
            _ => []
        };

    public static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        if (ColomboTimeZone.IsInvalidTime(local))
            throw new AppointmentValidationException("The selected local time does not exist in Asia/Colombo.");
        if (ColomboTimeZone.IsAmbiguousTime(local))
            throw new AppointmentValidationException("The selected local time is ambiguous in Asia/Colombo.");
        return TimeZoneInfo.ConvertTimeToUtc(local, ColomboTimeZone);
    }

    public static DateTime ToColombo(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ColomboTimeZone);

    private async Task ValidateScheduleAsync(
        Guid doctorId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        CancellationToken cancellationToken)
    {
        var day = GetScheduleDay(date);
        var fitsSchedule = await dbContext.DoctorSchedules.AnyAsync(schedule =>
            schedule.DoctorId == doctorId && schedule.DayOfWeek == day && schedule.IsActive &&
            schedule.StartTime <= start && schedule.EndTime >= end,
            cancellationToken);
        if (!fitsSchedule)
            throw new AppointmentValidationException("The entire appointment must fit within an active doctor schedule.");
    }

    private async Task EnsureNoOverlapAsync(
        Guid doctorId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludingAppointmentId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Appointments.Where(item => item.DoctorId == doctorId &&
            BlockingStatuses.Contains(item.Status) && item.StartAtUtc < endUtc && item.EndAtUtc > startUtc);
        if (excludingAppointmentId.HasValue)
            query = query.Where(item => item.Id != excludingAppointmentId.Value);

        if (await query.AnyAsync(cancellationToken))
            throw new AppointmentConflictException("The selected doctor already has an appointment during this time.");
    }

    private Task LockDoctorAsync(Guid doctorId, CancellationToken cancellationToken)
        => LockAsync(doctorId.ToString("D"), cancellationToken);

    private async Task LockAsync(string lockName, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
            return;

        await dbContext.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockName}, 0))) AS appointment_lock")
            .FirstAsync(cancellationToken);
    }

    private static AppointmentListItemViewModel ToListItem(Appointment item)
        => new()
        {
            Id = item.Id,
            AppointmentNumber = item.AppointmentNumber,
            PatientDisplayName = item.Patient.FirstName + " " + item.Patient.LastName,
            DoctorDisplayName = item.Doctor.FirstName + " " + item.Doctor.LastName,
            DepartmentName = item.Department.Name,
            StartAtLocal = ToColombo(item.StartAtUtc),
            EndAtLocal = ToColombo(item.EndAtUtc),
            Status = item.Status
        };

    private static int GetScheduleDay(DateOnly localDate)
    {
        var day = localDate.DayOfWeek;
        return day == DayOfWeek.Sunday ? 7 : (int)day;
    }

    private static (DateOnly Start, DateOnly End) GetLocalCalendarRange(DateOnly date, string view)
    {
        if (view == "day")
            return (date, date.AddDays(1));
        if (view == "month")
        {
            var start = new DateOnly(date.Year, date.Month, 1);
            return (start, start.AddMonths(1));
        }

        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return (monday, monday.AddDays(7));
    }

    private void ValidateDurationAndStart(TimeOnly start, int durationMinutes)
    {
        if (!_scheduling.AllowedDurationMinutes.Contains(durationMinutes))
            throw new AppointmentValidationException("Choose a configured appointment duration.");
        if (start.Ticks % TimeSpan.FromMinutes(_scheduling.StartIntervalMinutes).Ticks != 0)
            throw new AppointmentValidationException("Start time does not match the configured appointment interval.");
        if (start.AddMinutes(durationMinutes) <= start)
            throw new AppointmentValidationException("Overnight appointment windows are not supported.");
    }

    private static string NormalizeIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            throw new AppointmentValidationException("The booking request key is invalid. Reload the form and try again.");
        return key.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsExclusionViolation(DbUpdateException exception)
        => FindPostgresException(exception)?.SqlState == "23P01";

    private static bool IsUniqueViolation(DbUpdateException exception)
        => FindPostgresException(exception) is { SqlState: "23505", ConstraintName: "ix_appointments_actor_idempotency_key" };

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is PostgresException postgresException)
                return postgresException;
        return null;
    }

    private static TimeZoneInfo ResolveColomboTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
        }
    }
}

public sealed class AppointmentValidationException(string message) : Exception(message);
public sealed class AppointmentNotFoundException(Guid id) : Exception($"Appointment '{id}' was not found.");
public sealed class AppointmentConflictException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class AppointmentConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class AppointmentStatusException(string message) : Exception(message);
public sealed class AppointmentIdempotencyConflictException(string message, Exception? inner = null) : Exception(message, inner);
