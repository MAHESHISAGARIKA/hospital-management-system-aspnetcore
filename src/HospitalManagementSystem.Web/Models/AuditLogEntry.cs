using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

public sealed class AuditLogEntry
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? TargetUserId { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    [Required]
    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
