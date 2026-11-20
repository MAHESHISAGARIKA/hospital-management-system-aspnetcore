using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models.ViewModels;

public sealed class PatientEditViewModel : IValidatableObject
{
    public Guid Id { get; set; }

    [Display(Name = "Patient number")]
    public string? PatientNumber { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Date of birth")]
    [DataType(DataType.Date)]
    public DateOnly DateOfBirth { get; set; }

    [Required]
    [MaxLength(20)]
    public string Gender { get; set; } = string.Empty;

    [MaxLength(30)]
    [Display(Name = "NIC")]
    public string? NIC { get; set; }

    [MaxLength(32)]
    [Display(Name = "Phone number")]
    [Phone]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [MaxLength(254)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(10)]
    [Display(Name = "Blood group")]
    public string? BloodGroup { get; set; }

    [MaxLength(100)]
    [Display(Name = "Emergency contact name")]
    public string? EmergencyContactName { get; set; }

    [MaxLength(32)]
    [Phone]
    [Display(Name = "Emergency contact phone")]
    public string? EmergencyContactPhone { get; set; }

    public bool IsActive { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth == default)
        {
            yield return new ValidationResult("Enter a date of birth.", [nameof(DateOfBirth)]);
        }
        else if (DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            yield return new ValidationResult("Date of birth cannot be in the future.", [nameof(DateOfBirth)]);
        }

        var allowedGenders = new[] { "Female", "Male", "Intersex", "Unknown", "Prefer not to say" };
        if (!allowedGenders.Contains(Gender, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Choose a supported gender option.", [nameof(Gender)]);
        }

        if (!string.IsNullOrWhiteSpace(BloodGroup))
        {
            var allowed = new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-", "Unknown" };
            if (!allowed.Contains(BloodGroup, StringComparer.OrdinalIgnoreCase))
            {
                yield return new ValidationResult("Choose a valid blood group or Unknown.", [nameof(BloodGroup)]);
            }
        }
    }
}

public sealed class PatientSearchViewModel
{
    public string? Search { get; set; }
    public string? Gender { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "Name";
    public string SortDirection { get; set; } = "Asc";
    public IReadOnlyCollection<Patient> Patients { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class PatientProfileViewModel
{
    public Patient Patient { get; set; } = null!;
    public IReadOnlyCollection<PatientDocument> Documents { get; set; } = [];
}

public sealed class PatientDocumentUploadViewModel
{
    [Required]
    public Guid PatientId { get; set; }

    [Required]
    public IFormFile File { get; set; } = null!;
}
