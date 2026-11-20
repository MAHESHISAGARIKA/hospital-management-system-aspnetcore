namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class DashboardViewModel
{
    public string Email { get; set; } = string.Empty;

    public IReadOnlyCollection<string> Roles { get; set; } = [];
}
