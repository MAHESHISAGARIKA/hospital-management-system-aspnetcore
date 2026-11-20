using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
public sealed class DashboardController(UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Challenge();

        var roles = await userManager.GetRolesAsync(user);

        return View(new DashboardViewModel
        {
            Email = user.Email ?? string.Empty,
            Roles = roles.ToArray()
        });
    }
}
