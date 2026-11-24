using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Billing;

public sealed class BillingException(string message) : Exception(message);

public sealed partial class BillingService(HospitalDbContext db, IAuditLogService audit, TimeProvider clock)
{
    public const int PageSize = 25;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(AppointmentService.ToColombo(Now));

    public async Task<BillingPage<BillableService>> CatalogAsync(ClaimsPrincipal user, BillingFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, ct);
        var query = db.BillableServices.AsNoTracking().Where(s => !filter.Active.HasValue || s.Active == filter.Active);
        if (filter.Category.HasValue)
            query = query.Where(s => s.Category == filter.Category);
        if (Clean(filter.Search) is { } search)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(s => s.Code.Contains(term) || s.Name.ToUpper().Contains(term));
        }
        return await PageAsync(query.OrderBy(s => s.Code), filter, ct);
    }

    public async Task<BillableService> ServiceAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        return await db.BillableServices.AsNoTracking().Include(s => s.Prices).SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw await DeniedAsync(actor.Id, "Service", id, ct);
    }

    public async Task<Guid> SaveServiceAsync(ClaimsPrincipal user, Guid? id, BillingCatalogInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        Validate(input);
        var code = input.Code.Trim().ToUpperInvariant();
        var source = Clean(input.SourceCode)?.ToUpperInvariant();
        if (!Enum.IsDefined(input.Category) || code.Length > 40 || source is { Length: > 40 })
            throw new BillingException("Check the service code and category.");

        var validMapping = input.Category switch
        {
            BillingCategory.Laboratory => source is not null && await db.LaboratoryTests.AnyAsync(t => t.Code == source, ct),
            BillingCategory.Pharmacy => source is not null && await db.Medicines.AnyAsync(m => m.Code == source, ct),
            _ => source is null
        };
        if (!validMapping)
            throw new BillingException("Laboratory and pharmacy services need a valid catalog code. Other categories have no mapping code.");
        if (await db.BillableServices.AnyAsync(s => s.Code == code && s.Id != id, ct))
            throw new BillingException("This billing service code already exists.");

        await using var transaction = await BeginAsync(ct);
        if (id.HasValue) await LockServiceAsync(id.Value, ct);
        var service = id.HasValue ? await db.BillableServices.SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new BillingException("Service was not found.") :
            new BillableService { Id = Guid.CreateVersion7(), CreatedAtUtc = Now, CreatedByUserId = actor.Id };
        if (id.HasValue)
        {
            CheckToken(input.Token, service.Token);
            if ((service.Category != input.Category || service.SourceCode != source || service.Code != code) &&
                await db.BillingPrices.AnyAsync(p => p.ServiceId == id, ct))
                throw new BillingException("Create a new service to change an existing pricing identity or source mapping.");
        }
        else db.BillableServices.Add(service);

        (service.Code, service.Name, service.Category, service.SourceCode, service.Active) =
            (code, input.Name.Trim(), input.Category, source, input.Active);
        (service.Token, service.UpdatedAtUtc, service.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor.Id);
        await CommitAsync(transaction, id.HasValue ? "BillingServiceUpdated" : "BillingServiceCreated", actor.Id,
            $"Service {service.Id:D} {(service.Active ? "Active" : "Inactive")}", ct);
        return service.Id;
    }

    public async Task AddPriceAsync(ClaimsPrincipal user, Guid serviceId, BillingPriceInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        Money(input.Amount, true);
        if (!ValidDate(input.From) || input.Until is not { } until || !ValidDate(until) || until <= input.From)
            throw new BillingException("Enter a valid price period with an exclusive end date after the start.");
        await using var transaction = await BeginAsync(ct);
        await LockServiceAsync(serviceId, ct);
        if (!await db.BillableServices.AnyAsync(s => s.Id == serviceId, ct))
            throw await DeniedAsync(actor.Id, "Service", serviceId, ct);
        if (await db.BillingPrices.AnyAsync(p => p.ServiceId == serviceId &&
            (input.Until == null || p.From < input.Until) && (p.Until == null || p.Until > input.From), ct))
            throw new BillingException("The price period overlaps an existing price. Enter a different period.");
        var price = new BillingPrice { Id = Guid.CreateVersion7(), ServiceId = serviceId, From = input.From,
            Until = input.Until, Amount = input.Amount, CreatedAtUtc = Now, CreatedByUserId = actor.Id };
        db.BillingPrices.Add(price);
        await CommitAsync(transaction, "BillingPriceCreated", actor.Id, $"Service {serviceId:D} Price {price.Id:D} LKR {input.Amount:0.00}", ct);
    }

    private async Task<ApplicationUser> ActorAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole(HospitalRoles.Accountant) ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            throw new ClinicalAccessException();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor && u.IsEnabled, ct)
            ?? throw new ClinicalAccessException();
    }

    private async Task<ClinicalAccessException> DeniedAsync(Guid actor, string type, Guid id, CancellationToken ct)
    {
        await audit.WriteAsync("BillingAccessDenied", actor, null, $"{type} {id:D}", ct);
        return new();
    }

    private Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => db.Database.IsRelational()
        ? db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : db.Database.BeginTransactionAsync(ct);

    private Task LockServiceAsync(Guid id, CancellationToken ct)
        => LockAsync($"SELECT 1 AS \"Value\" FROM billing_services WHERE \"Id\" = {id} FOR UPDATE", ct);
    private Task LockInvoiceAsync(Guid id, CancellationToken ct)
        => LockAsync($"SELECT 1 AS \"Value\" FROM patient_invoices WHERE \"Id\" = {id} FOR UPDATE", ct);

    private async Task LockAsync(FormattableString query, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        try { await db.Database.SqlQuery<int>(query).ToListAsync(ct); }
        catch (PostgresException e) when (e.SqlState is "40P01" or "40001")
        { throw new BillingException("The record is busy. Reload and try again."); }
    }

    private async Task CommitAsync(IDbContextTransaction transaction, string action, Guid actor, string details, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(action, actor, null, details, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e) when (e is DbUpdateConcurrencyException ||
            (e is DbUpdateException ? e.InnerException : e) is PostgresException { SqlState: "23505" or "23503" or "23514" or "23P01" or "40001" or "40P01" })
        {
            db.ChangeTracker.Clear();
            throw new BillingException("Another transaction changed this record or used the same reference. Reload and check the current state.");
        }
    }

    private static bool ValidDate(DateOnly date) => date.Year is >= 2 and <= 9998;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Reason(string? value) => Clean(value) is { Length: <= 500 } reason
        ? reason : throw new BillingException("Enter a reason of up to 500 characters.");
    private static void Validate(object model)
    {
        if (!Validator.TryValidateObject(model, new ValidationContext(model), null, true))
            throw new BillingException("Check the required fields and their lengths.");
    }
    private static void Money(decimal amount, bool zero = false)
    {
        if (amount < 0 || !zero && amount == 0 || amount >= 10000000000000000m || decimal.Round(amount, 2) != amount)
            throw new BillingException("Enter a valid LKR amount with at most two decimal places.");
    }
    private static void CheckToken(Guid token, Guid current)
    {
        if (token == Guid.Empty || token != current)
            throw new BillingException("This form is stale. Reload it and try again.");
    }

    private static async Task<BillingPage<T>> PageAsync<T>(IQueryable<T> query, BillingFilter filter, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / PageSize)));
        return new(filter, await query.Skip((filter.Page - 1) * PageSize).Take(PageSize).ToArrayAsync(ct), total);
    }
}
