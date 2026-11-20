using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

/// <summary>
/// Represents a hospital department.
/// </summary>
public sealed class Department
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string DepartmentCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
