using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class DoctorEditViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    public string? DoctorNumber { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Display(Name = "Registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Specialization { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Qualifications { get; set; }

    [MaxLength(32)]
    [Phone]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [MaxLength(254)]
    [EmailAddress]
    public string? Email { get; set; }

    [Required]
    [Display(Name = "Department")]
    public Guid DepartmentId { get; set; }

    public Guid? ApplicationUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyToken { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(RegistrationNumber))
            yield return new ValidationResult("Enter a professional registration number.", [nameof(RegistrationNumber)]);

        if (DepartmentId == Guid.Empty)
            yield return new ValidationResult("Choose a department.", [nameof(DepartmentId)]);
    }
}

public sealed class DoctorSearchViewModel
{
    public string? Search { get; set; }
    public string? DoctorNumber { get; set; }
    public string? RegistrationNumber { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? Specialization { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "Name";
    public string SortDirection { get; set; } = "Asc";
    public IReadOnlyCollection<Doctor> Doctors { get; set; } = [];
    public IReadOnlyCollection<Department> Departments { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class DoctorScheduleViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }

    [Range(1, 7)]
    [Display(Name = "Day of week")]
    public int DayOfWeek { get; set; } = 1;

    [Required]
    [Display(Name = "Start time")]
    public TimeOnly StartTime { get; set; }

    [Required]
    [Display(Name = "End time")]
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyToken { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndTime <= StartTime)
            yield return new ValidationResult("End time must be later than start time.", [nameof(EndTime)]);
    }
}

public sealed class DoctorProfileViewModel
{
    public Doctor Doctor { get; set; } = null!;
    public IReadOnlyCollection<DoctorSchedule> Schedules { get; set; } = [];
}
