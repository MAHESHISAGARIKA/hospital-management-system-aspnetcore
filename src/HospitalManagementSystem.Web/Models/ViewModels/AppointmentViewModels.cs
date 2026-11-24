using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class AppointmentCreateViewModel : IValidatableObject
{
    [Required]
    public Guid PatientId { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }

    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly AppointmentDate { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    public int DurationMinutes { get; set; } = 30;

    [MaxLength(300)]
    [Display(Name = "Reason for visit")]
    public string? ReasonForVisit { get; set; }

    [MaxLength(500)]
    [Display(Name = "Administrative notes")]
    public string? Notes { get; set; }

    [Required]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyCollection<AppointmentOptionViewModel> Patients { get; set; } = [];
    public IReadOnlyCollection<DoctorOptionViewModel> Doctors { get; set; } = [];
    public IReadOnlyCollection<Department> Departments { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AppointmentDate == default)
            yield return new ValidationResult("Choose an appointment date.", [nameof(AppointmentDate)]);
    }
}

public sealed class AppointmentOptionViewModel
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class DoctorOptionViewModel
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AppointmentSearchViewModel
{
    public string? Search { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly? Date { get; set; }
    public AppointmentStatus? Status { get; set; }
    public bool UpcomingOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortDirection { get; set; } = "Asc";
    public int TotalCount { get; set; }
    public IReadOnlyCollection<AppointmentListItemViewModel> Appointments { get; set; } = [];

    public IReadOnlyCollection<DoctorOptionViewModel> Doctors { get; set; } = [];
    public IReadOnlyCollection<Department> Departments { get; set; } = [];
}

public sealed class AppointmentListItemViewModel
{
    public Guid Id { get; set; }
    public string AppointmentNumber { get; set; } = string.Empty;
    public string PatientDisplayName { get; set; } = string.Empty;
    public string DoctorDisplayName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime StartAtLocal { get; set; }
    public DateTime EndAtLocal { get; set; }
    public AppointmentStatus Status { get; set; }
}

public sealed class AppointmentDetailsViewModel
{
    public Appointment Appointment { get; set; } = null!;
    public DateTime StartAtLocal { get; set; }
    public DateTime EndAtLocal { get; set; }
    public IReadOnlyCollection<AppointmentStatus> AllowedNextStatuses { get; set; } = [];
}

public sealed class AppointmentRescheduleViewModel
{
    [Required]
    public Guid DoctorId { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly AppointmentDate { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    public int DurationMinutes { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public IReadOnlyCollection<DoctorOptionViewModel> Doctors { get; set; } = [];
    public IReadOnlyCollection<Department> Departments { get; set; } = [];

}

public sealed class AppointmentCancellationViewModel
{
    [Required]
    [StringLength(300, MinimumLength = 3)]
    [Display(Name = "Cancellation reason")]
    public string Reason { get; set; } = string.Empty;

    public Guid ConcurrencyToken { get; set; }
}

public sealed class AppointmentStatusViewModel
{
    [Required]
    public AppointmentStatus Status { get; set; }

    public Guid ConcurrencyToken { get; set; }
}

public sealed class AppointmentCalendarViewModel
{
    public DateOnly Date { get; set; }
    public string View { get; set; } = "week";
    public Guid? DoctorId { get; set; }
    public IReadOnlyCollection<AppointmentListItemViewModel> Appointments { get; set; } = [];

    public IReadOnlyCollection<DoctorOptionViewModel> Doctors { get; set; } = [];
}
