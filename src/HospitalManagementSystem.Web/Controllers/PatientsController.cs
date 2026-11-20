using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
public sealed class PatientsController(
    IPatientService patientService,
    HospitalDbContext dbContext,
    IAuditLogService auditLogService,
    IConfiguration configuration) : Controller
{
    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> Index(PatientSearchViewModel criteria, CancellationToken cancellationToken)
    {
        var result = await patientService.SearchAsync(criteria, cancellationToken);
        return View(result);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpGet]
    public IActionResult Create()
    {
        return View(new PatientEditViewModel
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow),
            Gender = "Unknown",
            IsActive = true
        });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PatientEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var duplicates = await patientService.FindPossibleDuplicatesAsync(model, cancellationToken);
            if (duplicates.Count > 0)
            {
                TempData["DuplicateWarning"] = "Possible duplicate patient records were found. Registration continues only as a deliberate action.";
            }

            var patient = await patientService.RegisterAsync(model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = $"Patient {patient.PatientNumber} registered successfully.";
            return RedirectToAction(nameof(Details), new { id = patient.Id });
        }
        catch (PatientDuplicateWarningException)
        {
            ModelState.AddModelError(string.Empty, "Possible duplicate patient records require review.");
        }
        catch (PatientValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (PatientNumberConflictException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return View(model);
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patientService.GetAsync(id, cancellationToken);
        if (patient is null)
            return NotFound();

        if (User.IsInRole(HospitalRoles.Receptionist) &&
            !User.IsInRole(HospitalRoles.Administrator) &&
            !User.IsInRole(HospitalRoles.Doctor))
        {
            await auditLogService.WriteAsync("PatientSensitiveRecordAccess", CurrentUserId(), null,
                $"Patient {patient.Id:D} demographic record viewed.", cancellationToken);
        }

        PatientDocument[] documents = [];
        if (User.IsInRole(HospitalRoles.Administrator) || User.IsInRole(HospitalRoles.Doctor))
        {
            documents = await dbContext.PatientDocuments
                .AsNoTracking()
                .Where(document => document.PatientId == id)
                .OrderByDescending(document => document.UploadedAtUtc)
                .ToArrayAsync(cancellationToken);
        }

        return View(new PatientProfileViewModel
        {
            Patient = patient,
            Documents = documents
        });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patientService.GetAsync(id, cancellationToken);
        if (patient is null)
            return NotFound();

        return View(new PatientEditViewModel
        {
            Id = patient.Id,
            PatientNumber = patient.PatientNumber,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            NIC = patient.NIC,
            PhoneNumber = patient.PhoneNumber,
            Email = patient.Email,
            Address = patient.Address,
            BloodGroup = patient.BloodGroup,
            EmergencyContactName = patient.EmergencyContactName,
            EmergencyContactPhone = patient.EmergencyContactPhone,
            IsActive = patient.IsActive,
            ConcurrencyToken = patient.ConcurrencyToken
        });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PatientEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            var patient = await patientService.UpdateAsync(model, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = $"Patient {patient.PatientNumber} updated successfully.";
            return RedirectToAction(nameof(Details), new { id = patient.Id });
        }
        catch (PatientConcurrencyException exception)
        {
            TempData["ConcurrencyError"] = exception.Message;
            return RedirectToAction(nameof(Edit), new { id = model.Id });
        }
        catch (PatientNotFoundException)
        {
            return NotFound();
        }
        catch (PatientValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            await patientService.ArchiveAsync(id, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Patient archived. The record was not deleted.";
        }
        catch (PatientNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        try
        {
            await patientService.ReactivateAsync(id, actorId.Value, cancellationToken);
            TempData["StatusMessage"] = "Patient reactivated.";
        }
        catch (PatientNotFoundException)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var patient = await patientService.GetAsync(id, cancellationToken);
        if (patient is null)
            return NotFound();

        var actorId = CurrentUserId();
        if (actorId is null)
            return Challenge();

        var result = await StoreDocumentAsync(patient, file, actorId.Value, cancellationToken);
        if (result.Error is not null)
        {
            TempData["DocumentError"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await auditLogService.WriteAsync("PatientDocumentUploaded", actorId, null,
            $"Document {result.Document!.Id:D} uploaded for patient {patient.Id:D}.", cancellationToken);

        TempData["StatusMessage"] = "Document uploaded securely.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = $"{HospitalRoles.Administrator},{HospitalRoles.Doctor}")]
    [HttpGet]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken cancellationToken)
    {
        var document = await dbContext.PatientDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (document is null)
            return NotFound();

        await auditLogService.WriteAsync("PatientDocumentDownloadAttempt", CurrentUserId(), null,
            $"Document {document.Id:D} for patient {document.PatientId:D} requested.", cancellationToken);

        var storageRoot = GetDocumentStorageRoot();
        var fullPath = ResolveStoredDocumentPath(storageRoot, document.StoredFileName);
        if (fullPath is null || !System.IO.File.Exists(fullPath))
            return NotFound();

        var stream = System.IO.File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new FileStreamResult(stream, document.ContentType)
        {
            FileDownloadName = document.OriginalFileName,
            EnableRangeProcessing = true
        };
    }

    private async Task<(PatientDocument? Document, string? Error)> StoreDocumentAsync(
        Patient patient,
        IFormFile file,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return (null, "Select a non-empty file.");

        var maxBytes = configuration.GetValue("PatientDocuments:MaxFileSizeBytes", 10 * 1024 * 1024);
        if (file.Length > maxBytes)
            return (null, "The document exceeds the configured 10 MB default limit.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            return (null, "Only PDF, JPG and PNG documents are allowed.");

        await using var signatureStream = file.OpenReadStream();
        var signature = new byte[8];
        var read = await signatureStream.ReadAsync(signature.AsMemory(0, signature.Length), cancellationToken);
        if (!HasApprovedFileSignature(extension, signature[..read]))
            return (null, "The file content does not match an approved document format.");

        var storageRoot = GetDocumentStorageRoot();
        Directory.CreateDirectory(storageRoot);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = ResolveStoredDocumentPath(storageRoot, storedName);
        if (fullPath is null)
            return (null, "The document storage path is invalid.");

        await using var source = file.OpenReadStream();
        await using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        try
        {
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);

            var document = new PatientDocument
            {
                Id = Guid.CreateVersion7(),
                PatientId = patient.Id,
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = storedName,
                ContentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => "application/octet-stream"
                },
                SizeBytes = file.Length,
                UploadedByUserId = actorId,
                UploadedAtUtc = DateTime.UtcNow
            };

            dbContext.PatientDocuments.Add(document);
            await dbContext.SaveChangesAsync(cancellationToken);
            return (document, null);
        }
        catch
        {
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
            throw;
        }
    }

    private string GetDocumentStorageRoot()
    {
        var configured = configuration["PatientDocuments:StorageRoot"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("PatientDocuments:StorageRoot is not configured.");

        return Path.GetFullPath(configured);
    }

    private static string? ResolveStoredDocumentPath(string storageRoot, string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) || Path.GetFileName(storedFileName) != storedFileName)
            return null;

        var combined = Path.GetFullPath(Path.Combine(storageRoot, storedFileName));
        var root = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static bool HasApprovedFileSignature(string extension, ReadOnlySpan<byte> bytes)
    {
        if (extension == ".pdf")
            return bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;

        if (extension is ".jpg" or ".jpeg")
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

        if (extension == ".png")
            return bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

        return false;
    }

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
