using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Inpatients;

public sealed class StayException(string message) : Exception(message);

public sealed partial class InpatientService(HospitalDbContext db, ClinicalRecordService clinical, IAuditLogService audit,
    UserManager<ApplicationUser> users, TimeProvider clock)
{
    public const int PageSize = 25;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(AppointmentService.ToColombo(Now));

    public async Task<IReadOnlyList<WardCensus>> WardsAsync(ClaimsPrincipal user, string? search, CancellationToken ct)
    {
        await ActorAsync(user, ct, HospitalRoles.Administrator, HospitalRoles.Receptionist);
        var query = db.HospitalWards.AsNoTracking();
        if (Clean(search) is { } term)
        {
            var key = term.ToUpperInvariant();
            query = query.Where(x => x.Code.Contains(key) || x.Name.ToUpper().Contains(key));
        }
        return await query.OrderBy(x => x.Code).Select(x => new WardCensus(x.Id, x.Code, x.Name, x.Active,
            x.Beds.Count(b => b.Active), x.Beds.Count(b => b.Allocations.Any(a => a.EndedAtUtc == null)),
            x.Active ? x.Beds.Count(b => b.Active && b.Condition == BedCondition.Ready &&
                !b.Allocations.Any(a => a.EndedAtUtc == null)) : 0)).ToArrayAsync(ct);
    }

    public async Task<Department[]> DepartmentsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        await ActorAsync(user, ct, HospitalRoles.Administrator);
        return await db.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToArrayAsync(ct);
    }

    public async Task<HospitalWard> WardAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Administrator, HospitalRoles.Receptionist);
        return await db.HospitalWards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Ward", id, ct);
    }

    public async Task<Guid> SaveWardAsync(ClaimsPrincipal user, Guid? id, WardInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Administrator);
        Validate(input);
        var code = input.Code.Trim().ToUpperInvariant();
        if (code.Length > 30 || await db.HospitalWards.AnyAsync(x => x.Code == code && x.Id != id, ct))
            throw new StayException("Enter a unique ward code of up to 30 characters.");
        if (input.DepartmentId.HasValue && !await db.Departments.AnyAsync(x => x.Id == input.DepartmentId && x.IsActive, ct))
            throw new StayException("Choose an active department.");
        await using var transaction = await BeginAsync(ct);
        if (id.HasValue) await LockWardAsync(id.Value, ct);
        var ward = id.HasValue ? await db.HospitalWards.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Ward", id.Value, ct)
            : new HospitalWard { Id = Guid.CreateVersion7(), CreatedAtUtc = Now, CreatedByUserId = actor };
        if (id.HasValue)
        {
            CheckToken(input.Token, ward.Token);
            if ((!input.Active || input.DepartmentId != ward.DepartmentId) &&
                await db.BedAllocations.AnyAsync(x => x.Bed.WardId == id && x.EndedAtUtc == null, ct))
                throw new StayException("A ward with occupied beds cannot be deactivated or moved to another department.");
        }
        else db.HospitalWards.Add(ward);
        (ward.Code, ward.Name, ward.Location, ward.DepartmentId, ward.Active) =
            (code, input.Name.Trim(), Clean(input.Location), input.DepartmentId, input.Active);
        (ward.Token, ward.UpdatedAtUtc, ward.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor);
        await CommitAsync(transaction, "InpatientWardSaved", actor, $"Ward {ward.Id:D} {(ward.Active ? "Active" : "Inactive")}", ct);
        return ward.Id;
    }

    public async Task<StayPage<HospitalBed>> BedsAsync(ClaimsPrincipal user, StayFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, ct, HospitalRoles.Administrator, HospitalRoles.Receptionist);
        var query = db.HospitalBeds.AsNoTracking().Include(x => x.Ward)
            .Include(x => x.Allocations.Where(a => a.EndedAtUtc == null)).AsQueryable();
        if (filter.WardId.HasValue) query = query.Where(x => x.WardId == filter.WardId);
        if (filter.Available == true) query = Available(query);
        if (filter.Available == false) query = query.Where(x => !x.Active || !x.Ward.Active || x.Condition != BedCondition.Ready ||
            x.Allocations.Any(a => a.EndedAtUtc == null));
        if (Clean(filter.Search) is { } term)
        {
            var key = term.ToUpperInvariant();
            query = query.Where(x => x.Label.Contains(key) || x.Ward.Code.Contains(key));
        }
        return await PageAsync(query.OrderBy(x => x.Ward.Code).ThenBy(x => x.Label), filter, ct);
    }

    public async Task<HospitalBed> BedAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Administrator, HospitalRoles.Receptionist);
        var bed = await db.HospitalBeds.AsNoTracking().Include(x => x.Ward).Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw await DeniedAsync(actor, "Bed", id, ct);
        foreach (var allocation in bed.Allocations)
            allocation.TransferReason = null;
        return bed;
    }

    public async Task<Guid> SaveBedAsync(ClaimsPrincipal user, Guid? id, BedInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct, HospitalRoles.Administrator);
        Validate(input);
        var label = input.Label.Trim().ToUpperInvariant();
        if (!Enum.IsDefined(input.Condition) || input.WardId == Guid.Empty || label.Length > 40)
            throw new StayException("Choose a ward, identifier and bed condition.");
        await using var transaction = await BeginAsync(ct);
        await LockWardAsync(input.WardId, ct);
        if (!await db.HospitalWards.AnyAsync(x => x.Id == input.WardId, ct))
            throw new StayException("This ward does not exist.");
        if (await db.HospitalBeds.AnyAsync(x => x.WardId == input.WardId && x.Label == label && x.Id != id, ct))
            throw new StayException("This bed identifier already exists in the ward.");
        if (id.HasValue) await LockBedAsync(id.Value, ct);
        var bed = id.HasValue ? await db.HospitalBeds.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Bed", id.Value, ct)
            : new HospitalBed { Id = Guid.CreateVersion7(), WardId = input.WardId, CreatedAtUtc = Now, CreatedByUserId = actor };
        if (id.HasValue)
        {
            CheckToken(input.Token, bed.Token);
            if (bed.WardId != input.WardId)
                throw new StayException("A registered bed keeps its ward. Register a new bed in the destination ward.");
            if (await db.BedAllocations.AnyAsync(x => x.BedId == id && x.EndedAtUtc == null, ct))
                throw new StayException("Occupied beds cannot be modified.");
        }
        else db.HospitalBeds.Add(bed);
        (bed.Label, bed.Condition, bed.Active) = (label, input.Condition, input.Active);
        Touch(bed, actor);
        await CommitAsync(transaction, "InpatientBedSaved", actor, $"Bed {bed.Id:D} {bed.Condition}", ct);
        return bed.Id;
    }

    private static IQueryable<HospitalBed> Available(IQueryable<HospitalBed> scope)
        => scope.Where(b => b.Active && b.Ward.Active && b.Condition == BedCondition.Ready && !b.Allocations.Any(a => a.EndedAtUtc == null));

    private async Task<Guid> ActorAsync(ClaimsPrincipal user, CancellationToken ct, params string[] roles)
    {
        if (user.Identity?.IsAuthenticated != true || !roles.Any(user.IsInRole) ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            !await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.IsEnabled, ct))
            throw new ClinicalAccessException();
        return actor;
    }

    private async Task<ClinicalAccessException> DeniedAsync(Guid actor, string entity, Guid id, CancellationToken ct)
    {
        await audit.WriteAsync("InpatientAccessDenied", actor, null, $"{entity} {id:D}", ct);
        return new();
    }

    private Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
        => db.Database.IsRelational() ? db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : db.Database.BeginTransactionAsync(ct);
    private Task LockWardAsync(Guid id, CancellationToken ct) => LockAsync($"SELECT 1 AS \"Value\" FROM hospital_wards WHERE \"Id\" = {id} FOR UPDATE", ct);
    private Task LockBedAsync(Guid id, CancellationToken ct) => LockAsync($"SELECT 1 AS \"Value\" FROM hospital_beds WHERE \"Id\" = {id} FOR UPDATE", ct);
    private Task LockAdmissionAsync(Guid id, CancellationToken ct) => LockAsync($"SELECT 1 AS \"Value\" FROM inpatient_admissions WHERE \"Id\" = {id} FOR UPDATE", ct);
    private async Task LockAsync(FormattableString sql, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        try { await db.Database.SqlQuery<int>(sql).ToListAsync(ct); }
        catch (PostgresException e) when (e.SqlState is "40P01" or "40001")
        { throw new StayException("The ward or admission is busy. Reload and try again."); }
    }

    private async Task CommitAsync(IDbContextTransaction tx, string action, Guid actor, string detail, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(action, actor, null, detail, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (Conflict(e))
        {
            db.ChangeTracker.Clear();
            throw new StayException("The bed or admission changed. Reload and choose an available bed.");
        }
    }
    private static bool Conflict(Exception e) => e is DbUpdateConcurrencyException ||
        (e is DbUpdateException ? e.InnerException : e) is PostgresException { SqlState: "23505" or "23503" or "23514" or "23P01" or "40001" or "40P01" };
    private void Touch(HospitalBed bed, Guid actor) => (bed.Token, bed.UpdatedAtUtc, bed.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor);
    private void Touch(InpatientAdmission adm, Guid actor) => (adm.Token, adm.UpdatedAtUtc, adm.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Reason(string? value) => Clean(value) is { Length: <= 500 } reason
        ? reason : throw new StayException("Enter a reason of up to 500 characters.");
    private static void Validate(object model)
    {
        if (!Validator.TryValidateObject(model, new ValidationContext(model), null, true))
            throw new StayException("Check the required fields and their lengths.");
    }
    private static void CheckToken(Guid token, Guid current)
    {
        if (token == Guid.Empty || token != current) throw new StayException("This record changed. Reload the page.");
    }
    private static async Task<StayPage<T>> PageAsync<T>(IQueryable<T> query, StayFilter filter, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / PageSize)));
        return new(filter, await query.Skip((filter.Page - 1) * PageSize).Take(PageSize).ToArrayAsync(ct), total);
    }
}
