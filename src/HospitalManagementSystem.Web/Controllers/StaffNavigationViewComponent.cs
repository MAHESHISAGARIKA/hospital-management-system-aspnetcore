using HospitalManagementSystem.Web.Services.Staff;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

public sealed class StaffNavigationViewComponent(StaffService staff) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        try
        {
            await staff.Mine(HttpContext.User, HttpContext.RequestAborted);
            return View();
        }
        catch (UnauthorizedAccessException) { return Content(""); }
    }
}
