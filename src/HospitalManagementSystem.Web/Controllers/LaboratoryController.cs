using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Laboratory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Laboratory")]
public sealed class LaboratoryController(LaboratoryService laboratory) : Controller
{
    private const string Team = $"{HospitalRoles.Doctor},{HospitalRoles.LaboratoryStaff}";

    [Authorize(Roles = Team)]
    [HttpGet("")]
    [HttpGet("Requests")]
    public Task<IActionResult> Index(LabQueueFilter filter, CancellationToken ct)
        => RunAsync(async () => ModelState.IsValid ? View(await laboratory.QueueAsync(User, filter, ct)) : BadRequest());

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpGet("Requests/Create/{id:guid}")]
    public Task<IActionResult> Create(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await laboratory.RequestPageAsync(User, id, null, ct)));

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Requests/Create/{id:guid}")]
    public Task<IActionResult> Create(Guid id, [Bind(Prefix = "Input")] LabOrderInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    return RedirectToAction(nameof(Details), new { id = await laboratory.RequestAsync(User, id, input, ct) });
                }
                catch (LaboratoryException error)
                {
                    ModelState.AddModelError("", error.Message);
                }
            }
            return View(await laboratory.RequestPageAsync(User, id, input, ct));
        });

    [Authorize(Roles = Team)]
    [HttpGet("Requests/Details/{id:guid}")]
    public Task<IActionResult> Details(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await laboratory.DetailsAsync(User, id, false, ct)));

    [Authorize(Roles = Team)]
    [HttpGet("Reports/{id:guid}")]
    public Task<IActionResult> Report(Guid id, CancellationToken ct)
        => RunAsync(async () => View("Details", await laboratory.DetailsAsync(User, id, true, ct)), id);

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Requests/Cancel/{id:guid}")]
    public Task<IActionResult> Cancel(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.CancelAsync(User, id, input, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Samples/Collect/{id:guid}")]
    public Task<IActionResult> Collect(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.CollectAsync(User, id, input, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Samples/Receive/{id:guid}")]
    public Task<IActionResult> Receive(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeSampleAsync(User, id, input, LabSampleStatus.Received, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Samples/Process/{id:guid}")]
    public Task<IActionResult> Process(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeSampleAsync(User, id, input, LabSampleStatus.Processing, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Samples/Reject/{id:guid}")]
    public Task<IActionResult> Reject(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeSampleAsync(User, id, input, LabSampleStatus.Rejected, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Results/Save/{id:guid}")]
    public Task<IActionResult> SaveResult(Guid id, LabResultInput input, bool amend, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.SaveResultAsync(User, id, input, amend, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Results/Submit/{id:guid}")]
    public Task<IActionResult> Submit(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeResultAsync(User, id, input, LabResultStatus.Submitted, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Results/Verify/{id:guid}")]
    public Task<IActionResult> Verify(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeResultAsync(User, id, input, LabResultStatus.Verified, ct));

    [Authorize(Roles = HospitalRoles.LaboratoryStaff)]
    [HttpPost("Results/Return/{id:guid}")]
    public Task<IActionResult> Return(Guid id, LabActionInput input, CancellationToken ct)
        => ChangeAsync(id, () => laboratory.ChangeResultAsync(User, id, input, LabResultStatus.Draft, ct));

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpGet("Tests")]
    public Task<IActionResult> Tests(string? search, CancellationToken ct)
        => RunAsync(async () => View(await laboratory.CatalogAsync(User, search, ct)));

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost("Tests/{id:guid?}")]
    public Task<IActionResult> SaveTest(Guid? id, LabCatalogInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            RequireValid();
            await laboratory.SaveTestAsync(User, id, input, ct);
            TempData["LabMessage"] = "Catalog test saved.";
            return RedirectToAction(nameof(Tests));
        }, catalog: true);

    private Task<IActionResult> ChangeAsync(Guid id, Func<Task> change)
        => RunAsync(async () =>
        {
            RequireValid();
            await change();
            TempData["LabMessage"] = "Laboratory record updated.";
            return RedirectToAction(nameof(Details), new { id });
        }, id);

    private void RequireValid()
    {
        if (!ModelState.IsValid)
            throw new LaboratoryException("Check the required fields and enter values in the expected format.");
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action, Guid? id = null, bool catalog = false)
    {
        try
        {
            return await action();
        }
        catch (ClinicalAccessException)
        {
            return Forbid();
        }
        catch (LaboratoryException error)
        {
            TempData["LabError"] = error.Message;
            return id.HasValue ? RedirectToAction(nameof(Details), new { id })
                : RedirectToAction(catalog ? nameof(Tests) : nameof(Index));
        }
    }
}
