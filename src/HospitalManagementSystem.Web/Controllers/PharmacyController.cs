using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Pharmacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Pharmacy")]
public sealed class PharmacyController(PharmacyService pharmacy) : Controller
{
    private const string CatalogTeam = $"{HospitalRoles.Administrator},{HospitalRoles.Pharmacist}";

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("")]
    public Task<IActionResult> Index(int? days, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.DashboardAsync(User, days, ct)));

    [Authorize(Roles = CatalogTeam)]
    [HttpGet("Medicines")]
    public Task<IActionResult> Medicines(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () => View("Medicines", await pharmacy.MedicinesAsync(User, filter, ct)), nameof(Medicines));

    [Authorize(Roles = CatalogTeam)]
    [HttpGet("Medicines/Create")]
    [HttpGet("Medicines/Edit/{id:guid}")]
    public Task<IActionResult> Medicine(Guid? id, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.MedicineAsync(User, id, ct)), nameof(Medicines));

    [Authorize(Roles = CatalogTeam)]
    [HttpPost("Medicines/Save/{id:guid?}")]
    public Task<IActionResult> SaveMedicine(Guid? id, MedicineCatalogInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await pharmacy.SaveMedicineAsync(User, id, input, ct);
                    TempData["PharmacyMessage"] = "Medicine saved.";
                    return RedirectToAction(nameof(Medicines));
                }
                catch (PharmacyException error) { ModelState.AddModelError("", error.Message); }
            }
            return View("Medicine", input);
        }, nameof(Medicines));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Inventory")]
    [HttpGet("Expiry")]
    public Task<IActionResult> Inventory(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () =>
        {
            ViewData["WarningDays"] = pharmacy.WarningDays;
            if (filter.Expired)
                ViewData["ExpiredTotals"] = await pharmacy.ExpiredTotalsAsync(User, filter, ct);
            return View(await pharmacy.InventoryAsync(User, filter, ct));
        });

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Stock/Receive")]
    public Task<IActionResult> Receive(CancellationToken ct)
        => RunAsync(async () => await ReceiveFormAsync(new() { RequestKey = Guid.NewGuid(), ReceivedDate = ClinicalRecordService.LocalToday }, ct));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpPost("Stock/Receive")]
    public Task<IActionResult> Receive(StockReceiptInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var id = await pharmacy.ReceiveAsync(User, input, ct);
                    TempData["PharmacyMessage"] = "Stock received.";
                    return RedirectToAction(nameof(Adjust), new { id });
                }
                catch (PharmacyException error) { ModelState.AddModelError("", error.Message); }
            }
            return await ReceiveFormAsync(input, ct);
        });

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Stock/Adjust/{id:guid}")]
    public Task<IActionResult> Adjust(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.BatchAsync(User, id, ct)));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpPost("Stock/Adjust/{id:guid}")]
    public Task<IActionResult> Adjust(Guid id, StockChangeInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            await pharmacy.AdjustAsync(User, id, input, ct);
            TempData["PharmacyMessage"] = "Stock change recorded.";
            return RedirectToAction(nameof(Adjust), new { id });
        }, nameof(Adjust), id);

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Prescriptions")]
    public Task<IActionResult> Prescriptions(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.PrescriptionsAsync(User, filter, ct)));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Prescriptions/Details/{id:guid}")]
    public Task<IActionResult> Prescription(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.PrescriptionAsync(User, id, ct)), nameof(Prescriptions));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpPost("Dispense/{id:guid}")]
    public Task<IActionResult> Dispense(Guid id, DispenseInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            var reference = await pharmacy.DispenseAsync(User, id, input, ct);
            return RedirectToAction(nameof(Receipt), new { id = reference });
        }, nameof(Prescription), id);

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("DispensingHistory")]
    public Task<IActionResult> History(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.HistoryAsync(User, filter, ct)));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("DispensingHistory/{id:guid}")]
    public Task<IActionResult> Receipt(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.ReceiptAsync(User, id, ct)));

    [Authorize(Roles = CatalogTeam)]
    [HttpGet("LowStock")]
    public Task<IActionResult> LowStock(PharmacyFilter filter, CancellationToken ct)
    {
        filter.LowStock = true;
        return Medicines(filter, ct);
    }

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Reports")]
    public Task<IActionResult> Reports(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () => View(await pharmacy.MovementsAsync(User, filter, ct)));

    [Authorize(Roles = HospitalRoles.Pharmacist)]
    [HttpGet("Reports/Consumption")]
    public Task<IActionResult> Consumption(PharmacyFilter filter, CancellationToken ct)
        => RunAsync(async () =>
        {
            ViewData["Filter"] = filter;
            return View(await pharmacy.ConsumptionAsync(User, filter, ct));
        });

    private async Task<IActionResult> ReceiveFormAsync(StockReceiptInput input, CancellationToken ct)
    {
        ViewData["Medicines"] = await pharmacy.ActiveMedicinesAsync(User, ct);
        return View("Receive", input);
    }

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action, string fallback = nameof(Index), Guid? id = null)
    {
        try
        {
            if (!ModelState.IsValid && (HttpMethods.IsGet(Request.Method) || id.HasValue))
                throw new PharmacyException("Check the submitted fields and quantity format.");
            return await action();
        }
        catch (ClinicalAccessException) { return Forbid(); }
        catch (PharmacyException error)
        {
            if (HttpMethods.IsGet(Request.Method) && string.Equals(ControllerContext.ActionDescriptor.ActionName, fallback, StringComparison.Ordinal))
                return BadRequest(error.Message);
            TempData["PharmacyError"] = error.Message;
            return RedirectToAction(fallback, new { id });
        }
    }
}
