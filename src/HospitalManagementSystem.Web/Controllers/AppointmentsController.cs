using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
public sealed class AppointmentsController(
    IAppointmentService appointmentService,
    HospitalDbContext dbContext,
    IAuditLogService auditLogService,
    IOptions<AppointmentSchedulingOptions> schedulingOptions) : Controller
{
    private readonly AppointmentSchedulingOptions _scheduling = schedulingOptions.Value;
    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> Index(AppointmentSearchViewModel criteria, CancellationToken cancellationToken)
    {
        var doctorAccountId = User.IsInRole(HospitalRoles.Doctor) && !User.IsInRole(HospitalRoles.Administrator)
            ? CurrentUserId()
            : null;
        if (User.IsInRole(HospitalRoles.Doctor) && !doctorAccountId.HasValue)
            return Forbid();

        var result = await appointmentService.SearchAsync(criteria, doctorAccountId, cancellationToken);
        return View(result);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        SetSchedulingViewData();
        var model = new AppointmentCreateViewModel
        {
            AppointmentDate = DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow).AddDays(1)),
            StartTime = new TimeOnly(9, 0),
            DurationMinutes = 30,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        await PopulateBookingOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AppointmentCreateViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            SetSchedulingViewData();
            await PopulateBookingOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var appointment = await appointmentService.BookAsync(model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = $"Appointment {appointment.AppointmentNumber} is scheduled.";
            return RedirectToAction(nameof(Details), new { id = appointment.Id });
        }
        catch (AppointmentIdempotencyConflictException)
        {
            ModelState.AddModelError(string.Empty, "This booking request was already submitted. Check the appointment directory before retrying.");
        }
        catch (Exception exception) when (exception is AppointmentValidationException or AppointmentConflictException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        await PopulateBookingOptionsAsync(model, cancellationToken);
        SetSchedulingViewData();
        return View(model);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await appointmentService.GetAsync(id, cancellationToken);
        if (appointment is null)
            return NotFound();
        if (!await CanAccessAsync(appointment, cancellationToken))
        {
            await auditLogService.WriteAsync("AppointmentAccessDenied", CurrentUserId(), null,
                $"Access denied for appointment {appointment.Id:D}.", cancellationToken);
            return Forbid();
        }

        return View(new AppointmentDetailsViewModel
        {
            Appointment = appointment,
            StartAtLocal = AppointmentService.ToColombo(appointment.StartAtUtc),
            EndAtLocal = AppointmentService.ToColombo(appointment.EndAtUtc),
            AllowedNextStatuses = GetAllowedStatusesForCurrentUser(appointment)
        });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await appointmentService.GetAsync(id, cancellationToken);
        if (appointment is null)
            return NotFound();
        if (appointment.Status != AppointmentStatus.Scheduled)
            return RedirectToAction(nameof(Details), new { id });

        var localStart = AppointmentService.ToColombo(appointment.StartAtUtc);
        var model = new AppointmentRescheduleViewModel
        {
            DoctorId = appointment.DoctorId,
            DepartmentId = appointment.DepartmentId,
            AppointmentDate = DateOnly.FromDateTime(localStart),
            StartTime = TimeOnly.FromDateTime(localStart),
            DurationMinutes = (int)(appointment.EndAtUtc - appointment.StartAtUtc).TotalMinutes,
            ConcurrencyToken = appointment.ConcurrencyToken
        };
        SetSchedulingViewData();
        await PopulateRescheduleOptionsAsync(model, cancellationToken);
        ViewBag.AppointmentId = appointment.Id;
        ViewBag.AppointmentNumber = appointment.AppointmentNumber;
        return View(model);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(Guid id, AppointmentRescheduleViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            SetSchedulingViewData();
            await PopulateRescheduleOptionsAsync(model, cancellationToken);
            ViewBag.AppointmentId = id;
            return View("Edit", model);
        }

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var updated = await appointmentService.RescheduleAsync(id, model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Appointment rescheduled.";
            return RedirectToAction(nameof(Details), new { id = updated.Id });
        }
        catch (Exception exception) when (exception is AppointmentValidationException or AppointmentConflictException or AppointmentConcurrencyException or AppointmentStatusException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            SetSchedulingViewData();
            await PopulateRescheduleOptionsAsync(model, cancellationToken);
            ViewBag.AppointmentId = id;
            return View("Edit", model);
        }
        catch (AppointmentNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, AppointmentCancellationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusError"] = "A cancellation reason of at least three characters is required.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            await appointmentService.CancelAsync(id, model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Appointment cancelled.";
        }
        catch (Exception exception) when (exception is AppointmentConcurrencyException or AppointmentStatusException or AppointmentValidationException)
        {
            TempData["StatusError"] = exception.Message;
        }
        catch (AppointmentNotFoundException)
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid id, AppointmentStatusViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest();

        var appointment = await appointmentService.GetAsync(id, cancellationToken);
        if (appointment is null)
            return NotFound();
        if (!await CanAccessAsync(appointment, cancellationToken))
        {
            await auditLogService.WriteAsync("AppointmentStatusAccessDenied", CurrentUserId(), null,
                $"Status update denied for appointment {appointment.Id:D}.", cancellationToken);
            return Forbid();
        }
        if (!GetAllowedStatusesForCurrentUser(appointment).Contains(model.Status))
            return Forbid();

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            await appointmentService.ChangeStatusAsync(id, model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Appointment status updated.";
        }
        catch (Exception exception) when (exception is AppointmentConcurrencyException or AppointmentStatusException)
        {
            TempData["StatusError"] = exception.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpGet]
    public async Task<IActionResult> AvailableSlots(Guid doctorId, DateOnly date, int durationMinutes, CancellationToken cancellationToken)
    {
        try
        {
            var slots = await appointmentService.GetAvailableSlotsAsync(doctorId, date, durationMinutes, cancellationToken);
            return Json(slots.Select(slot => new { value = slot.ToString("HH:mm"), text = slot.ToString("HH:mm") }));
        }
        catch (AppointmentValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> Calendar(DateOnly? date, string view = "week", Guid? doctorId = null, CancellationToken cancellationToken = default)
    {
        var selectedDate = date ?? DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow));
        var doctorAccountId = User.IsInRole(HospitalRoles.Doctor) && !User.IsInRole(HospitalRoles.Administrator)
            ? CurrentUserId()
            : null;
        if (User.IsInRole(HospitalRoles.Doctor) && !doctorAccountId.HasValue)
            return Forbid();

        var calendar = await appointmentService.GetCalendarAsync(selectedDate, view, doctorId, doctorAccountId, cancellationToken);
        return View(calendar);
    }

    private async Task PopulateBookingOptionsAsync(AppointmentCreateViewModel model, CancellationToken cancellationToken)
    {
        model.Patients = await dbContext.Patients.AsNoTracking()
            .Where(patient => patient.IsActive)
            .OrderBy(patient => patient.LastName).ThenBy(patient => patient.FirstName)
            .Select(patient => new AppointmentOptionViewModel
            {
                Id = patient.Id,
                DisplayName = patient.PatientNumber + " - " + patient.FirstName + " " + patient.LastName
            }).ToArrayAsync(cancellationToken);
        model.Departments = await dbContext.Departments.AsNoTracking()
            .Where(department => department.IsActive).OrderBy(department => department.Name).ToArrayAsync(cancellationToken);
        model.Doctors = await dbContext.Doctors.AsNoTracking()
            .Where(doctor => doctor.IsActive && doctor.DepartmentId == model.DepartmentId)
            .OrderBy(doctor => doctor.LastName).ThenBy(doctor => doctor.FirstName)
            .Select(doctor => new DoctorOptionViewModel
            {
                Id = doctor.Id,
                DisplayName = "Dr. " + doctor.FirstName + " " + doctor.LastName + " - " + doctor.Specialization
            }).ToArrayAsync(cancellationToken);
    }

    private void SetSchedulingViewData()
    {
        ViewBag.AllowedDurations = _scheduling.AllowedDurationMinutes;
        ViewBag.StartIntervalSeconds = _scheduling.StartIntervalMinutes * 60;
    }

    private async Task PopulateRescheduleOptionsAsync(AppointmentRescheduleViewModel model, CancellationToken cancellationToken)
    {
        model.Departments = await dbContext.Departments.AsNoTracking()
            .Where(department => department.IsActive).OrderBy(department => department.Name).ToArrayAsync(cancellationToken);
        model.Doctors = await dbContext.Doctors.AsNoTracking()
            .Where(doctor => doctor.IsActive && doctor.DepartmentId == model.DepartmentId)
            .OrderBy(doctor => doctor.LastName).ThenBy(doctor => doctor.FirstName)
            .Select(doctor => new DoctorOptionViewModel
            {
                Id = doctor.Id,
                DisplayName = "Dr. " + doctor.FirstName + " " + doctor.LastName + " - " + doctor.Specialization
            }).ToArrayAsync(cancellationToken);
    }

    private async Task<bool> CanAccessAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        if (User.IsInRole(HospitalRoles.Administrator) || User.IsInRole(HospitalRoles.Receptionist))
            return true;
        if (!User.IsInRole(HospitalRoles.Doctor))
            return false;

        var userId = CurrentUserId();
        return userId.HasValue && await dbContext.Doctors.AsNoTracking().AnyAsync(doctor =>
            doctor.Id == appointment.DoctorId && doctor.ApplicationUserId == userId.Value, cancellationToken);
    }

    private IReadOnlyCollection<AppointmentStatus> GetAllowedStatusesForCurrentUser(Appointment appointment)
    {
        if (User.IsInRole(HospitalRoles.Administrator) || User.IsInRole(HospitalRoles.Receptionist))
            return AppointmentService.AllowedNextStatuses(appointment.Status);
        if (User.IsInRole(HospitalRoles.Doctor) && appointment.Status == AppointmentStatus.Scheduled)
            return [AppointmentStatus.CheckedIn];
        return [];
    }

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
