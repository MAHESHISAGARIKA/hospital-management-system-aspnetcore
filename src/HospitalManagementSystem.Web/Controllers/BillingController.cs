using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Billing;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize(Roles = HospitalRoles.Accountant)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("Billing")]
public sealed class BillingController(BillingService billing) : Controller
{
    [HttpGet("")]
    public Task<IActionResult> Index(CancellationToken ct)
        => RunAsync(async () => View(await billing.DashboardAsync(User, ct)));

    [HttpGet("Services")]
    public Task<IActionResult> Services(BillingFilter filter, CancellationToken ct)
        => RunAsync(async () => View(await billing.CatalogAsync(User, filter, ct)));

    [HttpGet("Services/Create")]
    [HttpGet("Services/Edit/{id:guid}")]
    public Task<IActionResult> Service(Guid? id, CancellationToken ct)
        => RunAsync(async () =>
        {
            var service = id.HasValue ? await billing.ServiceAsync(User, id.Value, ct) : null;
            ViewData["Service"] = service;
            return View(service is null ? new BillingCatalogInput() : new BillingCatalogInput
            {
                Token = service.Token, Code = service.Code, Name = service.Name, Category = service.Category,
                SourceCode = service.SourceCode, Active = service.Active
            });
        });

    [HttpPost("Services/Save/{id:guid?}")]
    public Task<IActionResult> SaveService(Guid? id, BillingCatalogInput input, CancellationToken ct)
        => ChangeAsync(async () => RedirectToAction(nameof(Service), new { id = await billing.SaveServiceAsync(User, id, input, ct) }),
            nameof(Service), id);

    [HttpPost("Services/Prices/{id:guid}")]
    public Task<IActionResult> Price(Guid id, BillingPriceInput input, CancellationToken ct)
        => ChangeAsync(async () =>
        {
            await billing.AddPriceAsync(User, id, input, ct);
            return RedirectToAction(nameof(Service), new { id });
        }, nameof(Service), id);

    [HttpGet("Invoices")]
    [HttpGet("Outstanding")]
    public Task<IActionResult> Invoices(BillingFilter filter, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (Request.Path.Value?.EndsWith("/Outstanding", StringComparison.OrdinalIgnoreCase) == true)
                filter.Outstanding = true;
            return View(await billing.DirectoryAsync(User, filter, ct));
        });

    [HttpGet("Invoices/Create")]
    [HttpGet("Invoices/Edit/{id:guid}")]
    public Task<IActionResult> Edit(Guid? id, Guid? patientId, Guid? replacesId, CancellationToken ct)
        => RunAsync(async () => View(await billing.EditorAsync(User, id, patientId, replacesId, null, ct)));

    [HttpPost("Invoices/Save/{id:guid?}")]
    public Task<IActionResult> SaveDraft(Guid? id, [Bind(Prefix = "Input")] InvoiceInput input, CancellationToken ct)
        => RunAsync(async () =>
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var invoice = await billing.SaveDraftAsync(User, id, input, ct);
                    return RedirectToAction(nameof(Details), new { id = invoice });
                }
                catch (BillingException error)
                {
                    ModelState.AddModelError("", error.Message);
                }
            }
            return View("Edit", await billing.EditorAsync(User, id, input.PatientId, null, input, ct));
        }, nameof(Invoices));

    [HttpGet("Invoices/Details/{id:guid}")]
    [HttpGet("Payments/Create/{id:guid}")]
    public Task<IActionResult> Details(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await billing.InvoiceAsync(User, id, false, ct)));

    [HttpGet("Invoices/Print/{id:guid}")]
    public Task<IActionResult> Print(Guid id, CancellationToken ct)
        => RunAsync(async () =>
        {
            ViewData["Print"] = true;
            return View("Details", await billing.InvoiceAsync(User, id, true, ct));
        }, nameof(Details), id);

    [HttpPost("Invoices/Issue/{id:guid}")]
    public Task<IActionResult> Issue(Guid id, BillingTransitionInput input, CancellationToken ct)
        => ChangeAsync(async () =>
        {
            await billing.IssueAsync(User, id, input.Token, ct);
            return RedirectToAction(nameof(Details), new { id });
        }, nameof(Details), id);

    [HttpPost("Invoices/Void/{id:guid}")]
    public Task<IActionResult> Void(Guid id, BillingTransitionInput input, CancellationToken ct)
        => ChangeAsync(async () =>
        {
            await billing.VoidAsync(User, id, input, ct);
            return RedirectToAction(nameof(Details), new { id });
        }, nameof(Details), id);

    [HttpPost("Payments/Create/{id:guid}")]
    public Task<IActionResult> Pay(Guid id, PaymentInput input, CancellationToken ct)
        => ChangeAsync(async () => RedirectToAction(nameof(Receipt), new { id = await billing.PayAsync(User, id, input, ct) }),
            nameof(Details), id);

    [HttpPost("Payments/Reverse/{id:guid}")]
    public Task<IActionResult> Reverse(Guid id, BillingTransitionInput input, CancellationToken ct)
        => ChangeAsync(async () => RedirectToAction(nameof(Details), new { id = await billing.ReverseAsync(User, id, input, ct) }),
            nameof(Receipt), id);

    [HttpGet("Receipts/{id:guid}")]
    public Task<IActionResult> Receipt(Guid id, CancellationToken ct)
        => RunAsync(async () => View(await billing.ReceiptAsync(User, id, ct)));

    [HttpGet("Reports")]
    public Task<IActionResult> Reports(BillingFilter filter, CancellationToken ct)
        => RunAsync(async () => View(await billing.ReportsAsync(User, filter, ct)));

    private Task<IActionResult> ChangeAsync(Func<Task<IActionResult>> work, string fallback, Guid? id)
        => RunAsync(async () =>
        {
            if (!ModelState.IsValid)
                throw new BillingException("Check the submitted fields and value formats.");
            var result = await work();
            TempData["BillingMessage"] = "Billing record saved.";
            return result;
        }, fallback, id);

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> work, string? fallback = null, Guid? id = null)
    {
        try
        {
            if (HttpMethods.IsGet(Request.Method) && !ModelState.IsValid)
                return BadRequest("Check the filter values.");
            return await work();
        }
        catch (ClinicalAccessException) { return Forbid(); }
        catch (BillingException error)
        {
            if (fallback is null) return BadRequest(error.Message);
            TempData["BillingError"] = error.Message;
            return RedirectToAction(fallback, new { id });
        }
    }
}
