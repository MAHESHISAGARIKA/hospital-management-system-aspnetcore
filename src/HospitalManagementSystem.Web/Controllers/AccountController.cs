using System.Security.Claims;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuditLogService auditLogService) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(DashboardController.Index), "Dashboard");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !user.IsEnabled)
        {
            await auditLogService.WriteAsync("LoginFailed", null, null, "Invalid credentials or disabled account.");
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await auditLogService.WriteAsync("LoginSucceeded", user.Id, user.Id, null);

            if (user.RequirePasswordChange)
                return RedirectToAction(nameof(ChangePassword));

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
        }

        await auditLogService.WriteAsync("LoginFailed", user.Id, user.Id,
            result.IsLockedOut ? "Account locked out." : "Invalid credentials.");

        ModelState.AddModelError(string.Empty,
            result.IsLockedOut
                ? "This account is temporarily locked. Try again later."
                : "Invalid email or password.");

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await signInManager.SignOutAsync();

        Guid.TryParse(userId, out var actorId);
        await auditLogService.WriteAsync("Logout", actorId == Guid.Empty ? null : actorId, null, null);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        user.RequirePasswordChange = false;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await signInManager.RefreshSignInAsync(user);
        await auditLogService.WriteAsync("PasswordChanged", user.Id, user.Id, null);

        TempData["StatusMessage"] = "Your password was changed successfully.";
        return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
    }
}
