using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
public sealed class DoctorsController(
    IDoctorService doctorService,
    IDoctorScheduleService scheduleService,
    HospitalDbContext dbContext,
    UserManager<ApplicationUser> userManager) : Controller
{
    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor},{HospitalRoles.Receptionist},{HospitalRoles.Nurse},{HospitalRoles.LaboratoryStaff},{HospitalRoles.Pharmacist},{HospitalRoles.Accountant}")]
    [HttpGet]
    public async Task<IActionResult> Index(DoctorSearchViewModel criteria, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(HospitalRoles.Administrator) && !User.IsInRole(HospitalRoles.Doctor))
        {
            criteria.IsActive = true;
            criteria.RegistrationNumber = null;
        }

        var result = await doctorService.SearchAsync(criteria, cancellationToken);
        return View(result);
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
        ViewBag.DoctorAccounts = await DoctorAccountsAsync();
        return View(new DoctorEditViewModel { IsActive = true });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DoctorEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
            ViewBag.DoctorAccounts = await DoctorAccountsAsync();
            return View(model);
        }

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var duplicates = await doctorService.FindPossibleDuplicatesAsync(model, cancellationToken);
            if (duplicates.Count > 0)
                TempData["DuplicateWarning"] = "A possible duplicate registration number was found. Registration remains a deliberate action.";

            var doctor = await doctorService.RegisterAsync(model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = $"Doctor {doctor.DoctorNumber} registered successfully.";
            return RedirectToAction(nameof(Details), new { id = doctor.Id });
        }
        catch (Exception exception) when (exception is DoctorValidationException or DoctorDuplicateException or DoctorAccountLinkException or DoctorPersistenceException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
            ViewBag.DoctorAccounts = await DoctorAccountsAsync();
            return View(model);
        }
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor},{HospitalRoles.Receptionist},{HospitalRoles.Nurse},{HospitalRoles.LaboratoryStaff},{HospitalRoles.Pharmacist},{HospitalRoles.Accountant}")]
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var doctor = await doctorService.GetAsync(id, cancellationToken);
        if (doctor is null)
            return NotFound();

        if (User.IsInRole(HospitalRoles.Doctor) &&
            !User.IsInRole(HospitalRoles.Administrator) &&
            await IsOtherDoctorProfileAsync(doctor, cancellationToken))
        {
            return Forbid();
        }

        var schedules = await scheduleService.GetForDoctorAsync(id, cancellationToken);
        return View(new DoctorProfileViewModel
        {
            Doctor = doctor,
            Schedules = schedules
        });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var doctor = await doctorService.GetAsync(id, cancellationToken);
        if (doctor is null)
            return NotFound();

        ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
        ViewBag.DoctorAccounts = await DoctorAccountsAsync();

        return View(new DoctorEditViewModel
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
        });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DoctorEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
            ViewBag.DoctorAccounts = await DoctorAccountsAsync();
            return View(model);
        }

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var doctor = await doctorService.UpdateAsync(model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = $"Doctor {doctor.DoctorNumber} updated successfully.";
            return RedirectToAction(nameof(Details), new { id = doctor.Id });
        }
        catch (Exception exception) when (exception is DoctorConcurrencyException or DoctorValidationException or DoctorDuplicateException)
        {
            if (exception is DoctorConcurrencyException)
                TempData["ConcurrencyError"] = exception.Message;
            else
                ModelState.AddModelError(string.Empty, exception.Message);

            ViewBag.Departments = await ActiveDepartmentsAsync(cancellationToken);
            ViewBag.DoctorAccounts = await DoctorAccountsAsync();
            return View(model);
        }
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        await doctorService.DeactivateAsync(id, actorId.Value, cancellationToken);
        TempData["StatusMessage"] = "Doctor deactivated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        await doctorService.ReactivateAsync(id, actorId.Value, cancellationToken);
        TempData["StatusMessage"] = "Doctor reactivated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkAccount(Guid id, Guid applicationUserId, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            await doctorService.LinkAccountAsync(id, applicationUserId, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Doctor account linked.";
        }
        catch (Exception exception) when (exception is DoctorAccountLinkException or DoctorNotFoundException)
        {
            TempData["StatusError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlinkAccount(Guid id, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        await doctorService.UnlinkAccountAsync(id, actorId.Value, cancellationToken);
        TempData["StatusMessage"] = "Doctor account unlinked.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Schedule(DoctorScheduleViewModel model, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        if (User.IsInRole(HospitalRoles.Doctor) && !User.IsInRole(HospitalRoles.Administrator))
        {
            var ownUserId = actorId.Value;
            var doctor = await dbContext.Doctors.AsNoTracking().FirstOrDefaultAsync(item =>
                item.Id == model.DoctorId && item.ApplicationUserId == ownUserId, cancellationToken);
            if (doctor is null)
                return Forbid();
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusError"] = "Check the schedule fields and time range.";
            return RedirectToAction(nameof(Details), new { id = model.DoctorId });
        }

        try
        {
            if (model.Id == Guid.Empty)
                await scheduleService.AddAsync(model, actorId.Value, cancellationToken);
            else
                await scheduleService.UpdateAsync(model, actorId.Value, cancellationToken);

            TempData["StatusMessage"] = "Doctor schedule saved.";
        }
        catch (Exception exception) when (exception is DoctorScheduleValidationException or DoctorScheduleConflictException or DoctorScheduleConcurrencyException)
        {
            TempData["StatusError"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id = model.DoctorId });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateSchedule(Guid id, Guid doctorId, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        if (User.IsInRole(HospitalRoles.Doctor) && !User.IsInRole(HospitalRoles.Administrator))
        {
            var doctor = await dbContext.Doctors.AsNoTracking().FirstOrDefaultAsync(item =>
                item.Id == doctorId && item.ApplicationUserId == actorId.Value, cancellationToken);
            if (doctor is null)
                return Forbid();
        }

        await scheduleService.DeactivateAsync(id, actorId.Value, cancellationToken);
        TempData["StatusMessage"] = "Schedule deactivated.";
        return RedirectToAction(nameof(Details), new { id = doctorId });
    }

    private async Task<bool> IsOtherDoctorProfileAsync(Doctor doctor, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null)
            return true;

        return await dbContext.Doctors.AsNoTracking().AnyAsync(item =>
            item.Id == doctor.Id && item.ApplicationUserId != userId.Value, cancellationToken);
    }

    private async Task<IReadOnlyCollection<Department>> ActiveDepartmentsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Departments.AsNoTracking()
            .Where(department => department.IsActive)
            .OrderBy(department => department.Name)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<ApplicationUser>> DoctorAccountsAsync()
    {
        var users = await userManager.GetUsersInRoleAsync(HospitalRoles.Doctor);
        return users.Where(user => user.IsEnabled).ToArray();
    }

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
