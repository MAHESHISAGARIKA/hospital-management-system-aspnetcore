using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize(Roles = HospitalRoles.Administrator)]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    IAuditLogService auditLogService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = userManager.Users.OrderBy(user => user.Email).ToList();
        var items = new List<UserListItemViewModel>(users.Count);

        foreach (var user in users)
        {
            items.Add(new UserListItemViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                IsEnabled = user.IsEnabled,
                RequirePasswordChange = user.RequirePasswordChange,
                Roles = (await userManager.GetRolesAsync(user)).ToArray()
            });
        }

        return View(items);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (!HospitalRoles.All.Contains(model.Role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Role), "Choose one of the predefined hospital roles.");
            return View(model);
        }

        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "A user with this email already exists.");
            return View(model);
        }

        var actor = await userManager.GetUserAsync(User);
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            NormalizedUserName = normalizedEmail,
            EmailConfirmed = false,
            IsEnabled = true,
            RequirePasswordChange = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, model.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var roleResult = await userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "The staff role could not be assigned.");
            return View(model);
        }

        await auditLogService.WriteAsync(
            "UserCreated",
            actor?.Id,
            user.Id,
            "Staff account created; role assigned. No password or secret was recorded.");

        TempData["StatusMessage"] = "Staff account created. The user must change the temporary password on first login.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled(Guid id, bool isEnabled)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var actor = await userManager.GetUserAsync(User);

        if (!isEnabled && await IsLastActiveAdministratorAsync(user))
        {
            TempData["StatusMessage"] = "The last active Administrator cannot be deactivated.";
            return RedirectToAction(nameof(Index));
        }

        user.IsEnabled = isEnabled;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);

        await auditLogService.WriteAsync(
            isEnabled ? "UserActivated" : "UserDeactivated",
            actor?.Id,
            user.Id,
            null);

        TempData["StatusMessage"] = isEnabled ? "User activated." : "User deactivated. Existing sessions were invalidated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Roles(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        return View(new UpdateUserRolesViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            SelectedRoles = (await userManager.GetRolesAsync(user)).ToList(),
            AllowedRoles = HospitalRoles.All.ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Roles(UpdateUserRolesViewModel model)
    {
        var user = await userManager.FindByIdAsync(model.Id.ToString());
        if (user is null)
            return NotFound();

        if (model.SelectedRoles.Any(role => !HospitalRoles.All.Contains(role, StringComparer.Ordinal)))
        {
            ModelState.AddModelError(string.Empty, "Only predefined hospital roles may be assigned.");
            model.AllowedRoles = HospitalRoles.All.ToList();
            model.Email = user.Email ?? string.Empty;
            return View(model);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var actor = await userManager.GetUserAsync(User);

        if (!model.SelectedRoles.Contains(HospitalRoles.Administrator, StringComparer.Ordinal)
            && await IsLastActiveAdministratorAsync(user))
        {
            TempData["StatusMessage"] = "The last active Administrator must keep the Administrator role.";
            return RedirectToAction(nameof(Roles), new { id = user.Id });
        }

        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "The existing role assignments could not be changed.");
            model.AllowedRoles = HospitalRoles.All.ToList();
            model.Email = user.Email ?? string.Empty;
            return View(model);
        }

        if (model.SelectedRoles.Count > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, model.SelectedRoles);
            if (!addResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "The new role assignments could not be saved.");
                model.AllowedRoles = HospitalRoles.All.ToList();
                model.Email = user.Email ?? string.Empty;
                return View(model);
            }
        }

        await userManager.UpdateSecurityStampAsync(user);
        await auditLogService.WriteAsync(
            "UserRolesChanged",
            actor?.Id,
            user.Id,
            "Hospital role assignments changed.");

        TempData["StatusMessage"] = "Role assignments updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePasswordReset(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var actor = await userManager.GetUserAsync(User);

        _ = await userManager.GeneratePasswordResetTokenAsync(user);

        user.RequirePasswordChange = true;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);

        await auditLogService.WriteAsync(
            "PasswordResetInitiated",
            actor?.Id,
            user.Id,
            "Password change required; existing sessions invalidated. No email was sent.");

        TempData["StatusMessage"] = "Password reset initiated. The user must change the password after signing in; no email was sent.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> IsLastActiveAdministratorAsync(ApplicationUser targetUser)
    {
        if (!await userManager.IsInRoleAsync(targetUser, HospitalRoles.Administrator))
            return false;

        var administrators = await userManager.GetUsersInRoleAsync(HospitalRoles.Administrator);
        var activeAdministrators = administrators.Where(user =>
            user.IsEnabled &&
            (!user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow)).ToList();

        return activeAdministrators.Count == 1 && activeAdministrators[0].Id == targetUser.Id;
    }
}
