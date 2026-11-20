using Microsoft.AspNetCore.Identity;

namespace HospitalManagementSystem.Web.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;

    public bool RequirePasswordChange { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
