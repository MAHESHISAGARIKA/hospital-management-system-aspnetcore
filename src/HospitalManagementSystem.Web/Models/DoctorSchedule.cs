using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

public sealed class DoctorSchedule
{
    public Guid Id { get; set; }

    [Required]
    public Guid DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    [Range(1, 7)]
    public int DayOfWeek { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; set; }
}
