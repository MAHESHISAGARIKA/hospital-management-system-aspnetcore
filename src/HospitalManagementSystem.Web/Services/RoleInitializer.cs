using HospitalManagementSystem.Web.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HospitalManagementSystem.Web.Services;

public static class RoleInitializer
{
    public static async Task InitializeAsync(RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var roleName in HospitalRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Failed to initialize one or more predefined hospital roles.");
                }
            }
        }
    }
}
