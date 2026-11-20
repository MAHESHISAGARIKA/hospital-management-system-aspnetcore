using System.ComponentModel.DataAnnotations;

namespace HospitalManagementSystem.Web.Models;

public sealed class PatientDocument
{
    public Guid Id { get; set; }

    [Required]
    public Guid PatientId { get; set; }

    public Patient Patient { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTime UploadedAtUtc { get; set; }
}
