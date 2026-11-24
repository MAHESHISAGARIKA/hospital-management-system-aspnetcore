using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

public enum AppointmentStatus
{
    Scheduled,
    CheckedIn,
    Completed,
    Cancelled,
    Missed
}

public sealed class Appointment
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string AppointmentNumber { get; set; } = string.Empty;

    [Required]
    public Guid PatientId { get; set; }

    public Patient Patient { get; set; } = null!;

    [Required]
    public Guid DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    [Required]
    public Guid DepartmentId { get; set; }

    public Department Department { get; set; } = null!;

    public DateTime StartAtUtc { get; set; }

    public DateTime EndAtUtc { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    [MaxLength(300)]
    public string? ReasonForVisit { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public Guid? CancelledByUserId { get; set; }

    [Required]
    [MaxLength(64)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? BookingRequestFingerprint { get; init; }
    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid UpdatedByUserId { get; set; }

    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; }
}
