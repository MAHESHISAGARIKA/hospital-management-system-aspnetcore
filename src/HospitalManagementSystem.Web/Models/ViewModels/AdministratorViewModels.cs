using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class UserListItemViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool RequirePasswordChange { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = [];
}

public sealed class CreateUserViewModel
{
    [Required(ErrorMessage = "Enter the staff email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose an allowed role.")]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a temporary password.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "The temporary password must be at least 12 characters.")]
    [DataType(DataType.Password)]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm the temporary password.")]
    [Compare(nameof(TemporaryPassword), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmTemporaryPassword { get; set; } = string.Empty;
}

public sealed class UpdateUserRolesViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    public List<string> SelectedRoles { get; set; } = [];

    public List<string> AllowedRoles { get; set; } = [];
}

public sealed class ResetPasswordViewModel
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
