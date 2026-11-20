using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

/// <summary>
/// Represents a patient in the hospital system.
/// </summary>
public sealed class Patient
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string PatientNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[\p{L}\p{M}][\p{L}\p{M}' .-]*$", ErrorMessage = "Use a valid name.")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[\p{L}\p{M}][\p{L}\p{M}' .-]*$", ErrorMessage = "Use a valid name.")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required]
    [MaxLength(20)]
    public string Gender { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? NIC { get; set; }

    [MaxLength(32)]
    [RegularExpression(@"^[0-9+() -]{7,32}$", ErrorMessage = "Use a valid phone number.")]
    public string? PhoneNumber { get; set; }

    [MaxLength(254)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(10)]
    public string? BloodGroup { get; set; }

    [MaxLength(100)]
    [RegularExpression(@"^[\p{L}\p{M}][\p{L}\p{M}' .-]*$", ErrorMessage = "Use a valid contact name.")]
    public string? EmergencyContactName { get; set; }

    [MaxLength(32)]
    [RegularExpression(@"^[0-9+() -]{7,32}$", ErrorMessage = "Use a valid emergency phone number.")]
    public string? EmergencyContactPhone { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; }
}
