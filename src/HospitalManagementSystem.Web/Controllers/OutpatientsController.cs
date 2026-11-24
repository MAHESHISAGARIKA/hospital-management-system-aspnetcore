using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Inpatients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize(Roles = $"{HospitalRoles.Doctor},{HospitalRoles.Receptionist}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Outpatients")]
public sealed class OutpatientsController(InpatientService stays, IAppointmentService appointments) : Controller
{
    [HttpGet("")]
    [HttpGet("Today")]
    public Task<IActionResult> Today(AppointmentSearchViewModel filter, CancellationToken ct) => ListAsync(filter, true, ct);

    [HttpGet("Visits")]
    public Task<IActionResult> Visits(AppointmentSearchViewModel filter, CancellationToken ct) => ListAsync(filter, false, ct);

    [HttpGet("Visit/{id:guid}")]
    public async Task<IActionResult> Visit(Guid id, CancellationToken ct)
    {
        try { return View(await stays.VisitAsync(User, id, appointments, ct)); }
        catch (ClinicalAccessException) { return Forbid(); }
    }

    private async Task<IActionResult> ListAsync(AppointmentSearchViewModel filter, bool today, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest("Choose valid filters.");
        try { return View("Index", await stays.OutpatientsAsync(User, filter, today, appointments, ct)); }
        catch (ClinicalAccessException) { return Forbid(); }
        catch (StayException error) { return BadRequest(error.Message); }
    }
}
