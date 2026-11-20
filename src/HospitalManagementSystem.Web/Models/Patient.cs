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
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    [MaxLength(32)]
    public string? PhoneNumber { get; set; }

    [MaxLength(254)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
