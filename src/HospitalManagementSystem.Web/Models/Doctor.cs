using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalManagementSystem.Web.Models;

/// <summary>
/// Represents a doctor in the hospital system.
/// </summary>
public sealed class Doctor
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique, server-generated doctor number (e.g., "DOC00001").
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string DoctorNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Professional registration identifier (e.g., SLMC number). Normalized to uppercase, no spaces.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Specialization { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Qualifications { get; set; }

    [MaxLength(32)]
    public string? PhoneNumber { get; set; }

    [MaxLength(254)]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>
    /// Optional link to an Identity account (ApplicationUser).
    /// </summary>
    public Guid? ApplicationUserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Used for optimistic concurrency protection.
    /// </summary>
    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; }

    // Note: Physical deletion is not allowed. Use IsActive for logical deletion.
    // RegistrationNumber uniqueness: must be unique after normalization (uppercase, no spaces).
}
