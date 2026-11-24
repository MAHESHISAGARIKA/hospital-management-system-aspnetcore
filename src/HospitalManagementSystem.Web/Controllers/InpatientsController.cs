using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Inpatients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Inpatients")]
public sealed class InpatientsController(InpatientService stays) : Controller
{
    private const string Configuration = $"{HospitalRoles.Administrator},{HospitalRoles.Receptionist}";
    private const string AdmissionTeam = $"{HospitalRoles.Doctor},{HospitalRoles.Receptionist},{HospitalRoles.Nurse}";
    private const string Registration = $"{HospitalRoles.Doctor},{HospitalRoles.Receptionist}";

    [Authorize(Roles = AdmissionTeam)]
    [HttpGet("")]
    [HttpGet("Admissions")]
    [HttpGet("CurrentPatients")]
    public Task<IActionResult> Index(StayFilter filter, CancellationToken ct) => RunAsync(async () =>
    {
        if (Request.Path.Value?.EndsWith("/CurrentPatients", StringComparison.OrdinalIgnoreCase) == true)
            filter.Status = AdmissionStatus.Admitted;
        if (User.IsInRole(HospitalRoles.Receptionist))
            ViewData["Census"] = await stays.WardsAsync(User, null, ct);
        return View(await stays.AdmissionsAsync(User, filter, ct));
    });

    [Authorize(Roles = Registration)]
    [HttpGet("Admissions/Create")]
    public Task<IActionResult> Create(Guid? encounterId, CancellationToken ct)
        => RunAsync(async () => View(await stays.EditorAsync(User, encounterId, null, ct)));

    [Authorize(Roles = Registration)]
    [HttpPost("Admissions/Create")]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] AdmissionInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    return RedirectToAction(nameof(Details), new { id = await stays.RegisterAsync(User, input, ct) });
                }
                catch (StayException error) { ModelState.AddModelError("", error.Message); }
            }
            return View(await stays.EditorAsync(User, input.EncounterId, input, ct));
        });

    [Authorize(Roles = AdmissionTeam)]
    [HttpGet("Admissions/Details/{id:guid}")]
    public Task<IActionResult> Details(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await stays.DetailsAsync(User, id, false, ct)));

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpGet("DischargeSummary/{id:guid}")]
    public Task<IActionResult> Summary(Guid id, CancellationToken ct)
        => RunAsync(async () => View("Details", await stays.DetailsAsync(User, id, true, ct)));

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Admissions/Approve/{id:guid}")]
    public Task<IActionResult> Approve(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.ApproveAsync(User, id, input, ct), nameof(Details), id);

    [Authorize(Roles = Registration)]
    [HttpPost("Admissions/Cancel/{id:guid}")]
    public Task<IActionResult> Cancel(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.CancelAsync(User, id, input, ct), nameof(Details), id);

    [Authorize(Roles = HospitalRoles.Receptionist)]
    [HttpPost("Admissions/Allocate/{id:guid}")]
    public Task<IActionResult> Allocate(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.AllocateAsync(User, id, input, false, ct), nameof(Details), id);

    [Authorize(Roles = HospitalRoles.Receptionist)]
    [HttpPost("Admissions/Transfer/{id:guid}")]
    public Task<IActionResult> Transfer(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.AllocateAsync(User, id, input, true, ct), nameof(Details), id);

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Admissions/Discharge/{id:guid}")]
    public Task<IActionResult> Discharge(Guid id, DischargeInput input, CancellationToken ct)
        => ChangeAsync(() => stays.DischargeAsync(User, id, input, ct), nameof(Details), id);

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Admissions/Assign/{id:guid}")]
    public Task<IActionResult> Assign(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.AssignAsync(User, id, input, false, ct), nameof(Details), id);

    [Authorize(Roles = HospitalRoles.Doctor)]
    [HttpPost("Admissions/EndAssignment/{id:guid}")]
    public Task<IActionResult> EndAssignment(Guid id, AdmissionAction input, CancellationToken ct)
        => ChangeAsync(() => stays.AssignAsync(User, id, input, true, ct), nameof(Details), id);

    [Authorize(Roles = $"{HospitalRoles.Doctor},{HospitalRoles.Nurse}")]
    [HttpPost("Admissions/Observation/{id:guid}")]
    public Task<IActionResult> Observation(Guid id, ObservationInput input, CancellationToken ct)
        => ChangeAsync(() => stays.ObserveAsync(User, id, input, ct), nameof(Details), id);

    [Authorize(Roles = Configuration)]
    [HttpGet("Wards")]
    public Task<IActionResult> Wards(string? search, CancellationToken ct)
        => RunAsync(async () => View(await stays.WardsAsync(User, search, ct)));

    [Authorize(Roles = Configuration)]
    [HttpGet("Wards/Edit/{id:guid?}")]
    public Task<IActionResult> Ward(Guid? id, CancellationToken ct)
        => RunAsync(async () =>
        {
            var ward = id.HasValue ? await stays.WardAsync(User, id.Value, ct) : null;
            if (ward is null && !User.IsInRole(HospitalRoles.Administrator)) return Forbid();
            ViewData["Ward"] = ward;
            if (User.IsInRole(HospitalRoles.Administrator)) ViewData["Departments"] = await stays.DepartmentsAsync(User, ct);
            return View();
        });

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost("Wards/Save/{id:guid?}")]
    public Task<IActionResult> SaveWard(Guid? id, WardInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            Valid();
            var target = await stays.SaveWardAsync(User, id, input, ct);
            return RedirectToAction(nameof(Ward), new { id = target });
        }, nameof(Ward), id);

    [Authorize(Roles = Configuration)]
    [HttpGet("Beds")]
    [HttpGet("BedAvailability")]
    public Task<IActionResult> Beds(StayFilter filter, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (Request.Path.Value?.EndsWith("/BedAvailability", StringComparison.OrdinalIgnoreCase) == true)
                filter.Available = true;
            ViewData["Wards"] = await stays.WardsAsync(User, null, ct);
            return View(await stays.BedsAsync(User, filter, ct));
        });

    [Authorize(Roles = Configuration)]
    [HttpGet("Beds/Edit/{id:guid?}")]
    public Task<IActionResult> Bed(Guid? id, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (!id.HasValue && !User.IsInRole(HospitalRoles.Administrator)) return Forbid();
            ViewData["Bed"] = id.HasValue ? await stays.BedAsync(User, id.Value, ct) : null;
            ViewData["Wards"] = await stays.WardsAsync(User, null, ct);
            return View();
        });

    [Authorize(Roles = HospitalRoles.Administrator)]
    [HttpPost("Beds/Save/{id:guid?}")]
    public Task<IActionResult> SaveBed(Guid? id, BedInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            Valid();
            var target = await stays.SaveBedAsync(User, id, input, ct);
            return RedirectToAction(nameof(Bed), new { id = target });
        }, nameof(Bed), id);

    private Task<IActionResult> ChangeAsync(Func<Task> act, string next, Guid id) => RunAsync(async () =>
    {
        Valid();
        await act();
        TempData["StayMessage"] = "Admission record updated.";
        return RedirectToAction(next, new { id });
    }, next, id);

    private void Valid()
    {
        if (!ModelState.IsValid) throw new StayException("Check the submitted fields.");
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> act, string? fallback = null, Guid? id = null)
    {
        try
        {
            if (HttpMethods.IsGet(Request.Method)) Valid();
            return await act();
        }
        catch (ClinicalAccessException) { return Forbid(); }
        catch (StayException error)
        {
            if (fallback is null) return BadRequest(error.Message);
            TempData["StayError"] = error.Message;
            return RedirectToAction(fallback, new { id });
        }
    }
}
