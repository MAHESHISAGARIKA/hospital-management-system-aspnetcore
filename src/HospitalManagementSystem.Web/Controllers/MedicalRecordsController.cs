using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize(Roles = HospitalRoles.Doctor)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AutoValidateAntiforgeryToken]
public sealed class MedicalRecordsController(ClinicalRecordService clinical) : Controller
{
    [HttpGet]
    public Task<IActionResult> Index(CancellationToken ct)
        => ExecuteAsync(async () => View(await clinical.OverviewAsync(User, ct)));

    [HttpPost]
    public Task<IActionResult> Open(Guid appointmentId, CancellationToken ct)
        => ExecuteAsync(async () =>
        {
            var id = await clinical.OpenAsync(User, appointmentId, ct);
            return RedirectToAction(nameof(Encounter), new { id });
        });

    [HttpGet]
    public Task<IActionResult> Encounter(Guid id, CancellationToken ct)
        => ExecuteAsync(async () => View("ClinicalReport", await clinical.EncounterAsync(User, id, false, null, ct)), id);

    [HttpGet]
    public Task<IActionResult> Report(Guid id, CancellationToken ct)
        => ExecuteAsync(async () => View("ClinicalReport", await clinical.EncounterAsync(User, id, true, null, ct)), id);

    [HttpGet]
    public Task<IActionResult> Prescription(Guid id, Guid recordId, bool print, CancellationToken ct)
        => ExecuteAsync(async () => View("ClinicalReport", await clinical.EncounterAsync(User, id, print, recordId, ct)), id);

    [HttpGet]
    public Task<IActionResult> Patient(Guid id, DateOnly? from, DateOnly? to, Guid? encounterId, CancellationToken ct)
        => ExecuteAsync(async () => ModelState.IsValid
            ? View("ClinicalReport", await clinical.PatientAsync(User, id, from, to, encounterId, ct))
            : BadRequest());

    [HttpGet]
    public Task<IActionResult> Entry(Guid id, Guid? recordId, ClinicalRecordKind kind, bool amend, CancellationToken ct)
        => ExecuteAsync(async () =>
        {
            if (!ModelState.IsValid || !Enum.IsDefined(kind))
                return BadRequest();

            return View(await clinical.EditorAsync(User, id, recordId, kind, amend, ct));
        }, id);

    [HttpPost]
    public Task<IActionResult> SaveEntry(Guid id, Guid? recordId, bool amend, [Bind(Prefix = "Input")] ClinicalEntryInput input, CancellationToken ct)
        => ExecuteAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await clinical.SaveRecordAsync(User, id, recordId, amend, input, ct);
                    TempData["ClinicalMessage"] = "Draft saved.";
                    return RedirectToAction(nameof(Encounter), new { id });
                }
                catch (ClinicalWorkflowException exception)
                {
                    ModelState.AddModelError(string.Empty, exception.Message);
                }
            }

            var editor = await clinical.EditorAsync(User, id, recordId, input.Kind, amend, ct);
            input.Kind = editor.Input.Kind;
            return View("Entry", editor with { Input = input });
        }, id);

    [HttpPost]
    public Task<IActionResult> FinalizeEntry(Guid id, Guid recordId, ClinicalTransitionInput input, CancellationToken ct)
        => TransitionAsync(id, () => clinical.FinalizeRecordAsync(User, id, recordId, input, ct), "Entry finalized.");

    [HttpPost]
    public Task<IActionResult> VoidPrescription(Guid id, Guid recordId, ClinicalTransitionInput input, CancellationToken ct)
        => TransitionAsync(id, () => clinical.VoidPrescriptionAsync(User, id, recordId, input, ct), "Prescription void recorded.");

    [HttpPost]
    public Task<IActionResult> FinalizeEncounter(Guid id, Guid encounterToken, CancellationToken ct)
        => TransitionAsync(id, () => clinical.FinalizeEncounterAsync(User, id, encounterToken, ct), "Encounter finalized.");

    private Task<IActionResult> TransitionAsync(Guid id, Func<Task> transition, string message)
        => ExecuteAsync(async () =>
        {
            if (!ModelState.IsValid)
                throw new ClinicalWorkflowException("Check the form and try again.");

            await transition();
            TempData["ClinicalMessage"] = message;
            return RedirectToAction(nameof(Encounter), new { id });
        }, id);

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action, Guid? encounterId = null)
    {
        try
        {
            return await action();
        }
        catch (ClinicalAccessException)
        {
            return Forbid();
        }
        catch (ClinicalWorkflowException exception)
        {
            TempData["ClinicalError"] = exception.Message;
            return encounterId.HasValue
                ? RedirectToAction(nameof(Encounter), new { id = encounterId })
                : RedirectToAction(nameof(Index));
        }
    }
}
