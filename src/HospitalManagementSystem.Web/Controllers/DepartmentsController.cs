using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Controllers;

[Authorize(Roles = HospitalRoles.Administrator)]
public sealed class DepartmentsController(
    HospitalDbContext dbContext,
    IAuditLogService auditLogService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(await dbContext.Departments.AsNoTracking().OrderBy(department => department.Name).ToArrayAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View(new Department());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Department model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        model.Id = Guid.CreateVersion7();
        model.DepartmentCode = model.DepartmentCode.Trim().ToUpperInvariant();
        model.Name = model.Name.Trim();
        model.IsActive = true;
        model.CreatedAtUtc = DateTime.UtcNow;
        model.UpdatedAtUtc = DateTime.UtcNow;

        if (await dbContext.Departments.AnyAsync(department =>
            department.DepartmentCode == model.DepartmentCode || department.Name == model.Name, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Department code or name already exists.");
            return View(model);
        }

        dbContext.Departments.Add(model);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actorId = CurrentUserId();
        await auditLogService.WriteAsync("DepartmentCreated", actorId, null,
            $"Department {model.Id:D} created.", cancellationToken);

        TempData["StatusMessage"] = "Department created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments.FindAsync([id], cancellationToken);
        return department is null ? NotFound() : View(department);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Department model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var department = await dbContext.Departments.FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (department is null)
            return NotFound();

        department.DepartmentCode = model.DepartmentCode.Trim().ToUpperInvariant();
        department.Name = model.Name.Trim();
        department.IsActive = model.IsActive;
        department.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actorId = CurrentUserId();
        await auditLogService.WriteAsync("DepartmentUpdated", actorId, null,
            $"Department {department.Id:D} updated.", cancellationToken);

        TempData["StatusMessage"] = "Department updated.";
        return RedirectToAction(nameof(Index));
    }

    private Guid? CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
