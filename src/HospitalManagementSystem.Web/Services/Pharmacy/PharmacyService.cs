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
using Microsoft.Extensions.Options;
using Npgsql;

namespace HospitalManagementSystem.Web.Services.Pharmacy;

public sealed class PharmacyException(string message) : Exception(message);

public sealed class PharmacyOptions
{
    public int[] WarningDays { get; set; } = [30, 60, 90];
}

public sealed class PharmacyService(HospitalDbContext db, ClinicalRecordService clinical, IAuditLogService audit,
    TimeProvider clock, IOptions<PharmacyOptions> options)
{
    public const int PageSize = 25;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(AppointmentService.ToColombo(Now));
    public IReadOnlyList<int> WarningDays => options.Value.WarningDays;

    public async Task<PharmacyPage<MedicineStock>> MedicinesAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        await CatalogActorAsync(user, ct);
        var query = Stock().Where(row => !filter.Active.HasValue || row.Medicine.IsActive == filter.Active);
        if (Clean(filter.Search) is { } search)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(row => row.Medicine.Code.Contains(term) || row.Medicine.GenericName.ToUpper().Contains(term) ||
                (row.Medicine.BrandName != null && row.Medicine.BrandName.ToUpper().Contains(term)));
        }
        if (filter.LowStock)
            query = query.Where(row => row.Medicine.IsActive && (row.Eligible == 0 || row.Eligible < row.Medicine.ReorderThreshold));
        return await PageAsync(query.OrderBy(row => row.Medicine.GenericName).ThenBy(row => row.Medicine.Id), filter, ct);
    }

    public async Task<MedicineCatalogInput> MedicineAsync(ClaimsPrincipal user, Guid? id, CancellationToken ct)
    {
        await CatalogActorAsync(user, ct);
        if (!id.HasValue)
            return new();
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new PharmacyException("Medicine was not found.");
        return new() { Code = medicine.Code, GenericName = medicine.GenericName, BrandName = medicine.BrandName,
            Strength = medicine.Strength, Form = medicine.Form, Unit = medicine.Unit, Manufacturer = medicine.Manufacturer,
            Description = medicine.Description, ReorderThreshold = medicine.ReorderThreshold, IsActive = medicine.IsActive, Token = medicine.ConcurrencyToken };
    }

    public async Task SaveMedicineAsync(ClaimsPrincipal user, Guid? id, MedicineCatalogInput input, CancellationToken ct)
    {
        var actor = await CatalogActorAsync(user, ct);
        Validate(input);
        Quantity(input.ReorderThreshold, input.Unit, allowZero: true);
        var code = input.Code.Trim().ToUpperInvariant();
        if (code.Length > 30 || await db.Medicines.AnyAsync(m => m.Code == code && m.Id != id, ct))
            throw new PharmacyException("Enter a unique medicine code of up to 30 characters.");
        await using var transaction = await BeginAsync(ct);
        if (id.HasValue)
            await LockMedicineAsync(id.Value, ct);

        var medicine = id.HasValue ? await db.Medicines.SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new PharmacyException("Medicine was not found.") :
            new Medicine { Id = Guid.CreateVersion7(), CreatedAtUtc = Now, CreatedByUserId = actor };

        if (id.HasValue)
        {
            CheckToken(input.Token, medicine.ConcurrencyToken);
            if (await db.MedicineBatches.AnyAsync(b => b.MedicineId == id, ct) &&
                (medicine.Unit != input.Unit || !Same(medicine.GenericName, input.GenericName) ||
                 !Same(medicine.Strength, input.Strength) || !Same(medicine.Form, input.Form) || !Same(medicine.BrandName, input.BrandName)))
                throw new PharmacyException("Use a new catalog record to change the identity or unit of stocked medicine.");
        }
        else
            db.Medicines.Add(medicine);

        medicine.Code = code;
        medicine.GenericName = input.GenericName.Trim();
        medicine.BrandName = Clean(input.BrandName);
        medicine.Form = input.Form.Trim();
        medicine.Strength = input.Strength.Trim();
        medicine.Unit = input.Unit;
        medicine.Manufacturer = Clean(input.Manufacturer);
        medicine.Description = Clean(input.Description);
        medicine.ReorderThreshold = input.ReorderThreshold;
        medicine.IsActive = input.IsActive;
        (medicine.ConcurrencyToken, medicine.UpdatedAtUtc, medicine.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor);
        await CommitAsync(transaction, id.HasValue ? "PharmacyMedicineUpdated" : "PharmacyMedicineRegistered",
            actor, $"Medicine {medicine.Id:D} {(medicine.IsActive ? "Active" : "Inactive")}", ct);
    }

    public async Task<Medicine[]> ActiveMedicinesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        return await db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.GenericName).ThenBy(m => m.Code).ToArrayAsync(ct);
    }

    public async Task<Guid> ReceiveAsync(ClaimsPrincipal user, StockReceiptInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        Validate(input);
        RequireKey(input.RequestKey);
        await using var transaction = await BeginAsync(ct);
        await LockRequestAsync(actor, input.RequestKey, ct);
        var prior = await FindMovementAsync(actor, input.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.Kind == StockMovementKind.Received && prior.Batch.MedicineId == input.MedicineId &&
                Same(prior.Batch.Number, input.Number) && prior.Quantity == input.Quantity && prior.Batch.Expiry == input.Expiry &&
                prior.Batch.ReceivedDate == input.ReceivedDate && prior.Batch.Supplier == Clean(input.Supplier) &&
                prior.Batch.Notes == Clean(input.Notes))
                return prior.BatchId;
            throw new PharmacyException("This request key was already used for different stock work.");
        }

        await LockMedicineAsync(input.MedicineId, ct);
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == input.MedicineId && m.IsActive, ct)
            ?? throw new PharmacyException("Choose an active medicine.");
        Quantity(input.Quantity, medicine.Unit);
        var number = input.Number.Trim().ToUpperInvariant();
        if (number.Length > 60 || input.ReceivedDate == default || input.ReceivedDate > Today ||
            input.Expiry < input.ReceivedDate || input.Expiry < Today)
            throw new PharmacyException("Enter a valid batch, receipt date and unexpired stock date.");
        if (await db.MedicineBatches.AnyAsync(b => b.MedicineId == medicine.Id && b.Number == number, ct))
            throw new PharmacyException("This medicine already has that batch number.");

        var batch = new MedicineBatch
        {
            Id = Guid.CreateVersion7(), MedicineId = medicine.Id, Number = number, Supplier = Clean(input.Supplier),
            Notes = Clean(input.Notes), Unit = medicine.Unit, Expiry = input.Expiry, ReceivedDate = input.ReceivedDate,
            ReceivedQuantity = input.Quantity, Available = input.Quantity, CreatedAtUtc = Now, ReceivedByUserId = actor
        };
        Touch(batch, actor);
        db.MedicineBatches.Add(batch);
        Movement(batch, input.Quantity, StockMovementKind.Received, actor, input.RequestKey);
        await CommitAsync(transaction, "PharmacyStockReceived", actor, $"Batch {batch.Id:D} {input.Quantity} {batch.Unit}", ct);
        return batch.Id;
    }

    public async Task<MedicineBatch> BatchAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        return await db.MedicineBatches.AsNoTracking().Include(b => b.Medicine).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new PharmacyException("Stock batch was not found.");
    }

    public async Task AdjustAsync(ClaimsPrincipal user, Guid id, StockChangeInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        Validate(input);
        RequireKey(input.RequestKey);
        await using var transaction = await BeginAsync(ct);
        await LockRequestAsync(actor, input.RequestKey, ct);
        if (await FindMovementAsync(actor, input.RequestKey, ct) is { } prior)
        {
            if (prior.BatchId == id && prior.Kind == input.Kind && prior.Quantity == input.Quantity && prior.Reason == Clean(input.Reason))
                return;
            throw new PharmacyException("This request key was already used for different stock work.");
        }

        var medicine = await db.MedicineBatches.AsNoTracking().Where(b => b.Id == id).Select(b => (Guid?)b.MedicineId).SingleOrDefaultAsync(ct)
            ?? throw new PharmacyException("Stock batch was not found.");
        await LockMedicineAsync(medicine, ct);
        var batch = await db.MedicineBatches.SingleAsync(b => b.Id == id, ct);
        CheckToken(input.Token, batch.ConcurrencyToken);
        var priorStatus = batch.Status;
        switch (input.Kind)
        {
            case StockMovementKind.Quarantined when batch.Status == BatchStatus.Available && input.Quantity == 0:
                batch.Status = BatchStatus.Quarantined;
                break;
            case StockMovementKind.Recalled when batch.Status != BatchStatus.Recalled && input.Quantity == 0:
                batch.Status = BatchStatus.Recalled;
                break;
            case StockMovementKind.Increase when batch.Status == BatchStatus.Available && batch.Expiry >= Today:
                Quantity(input.Quantity, batch.Unit);
                Quantity(batch.Available + input.Quantity, batch.Unit);
                batch.Available += input.Quantity;
                break;
            case StockMovementKind.Decrease when batch.Status == BatchStatus.Available && batch.Expiry >= Today:
            case StockMovementKind.Removed when batch.Status != BatchStatus.Available || batch.Expiry < Today:
                Quantity(input.Quantity, batch.Unit);
                if (input.Quantity > batch.Available)
                    throw new PharmacyException("The requested decrease exceeds the recorded balance.");
                batch.Available -= input.Quantity;
                break;
            default:
                throw new PharmacyException("That adjustment is unavailable for this batch. Status changes require zero quantity.");
        }
        Touch(batch, actor);
        Movement(batch, input.Quantity, input.Kind, actor, input.RequestKey, Clean(input.Reason));
        await CommitAsync(transaction, "PharmacyStockAdjusted", actor,
            $"Batch {batch.Id:D} {input.Kind} {input.Quantity} {batch.Unit} {priorStatus} to {batch.Status}", ct);
    }

    public async Task<PharmacyPage<MedicineBatch>> InventoryAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var today = Today;
        var query = db.MedicineBatches.AsNoTracking().Include(b => b.Medicine).AsQueryable();
        if (filter.Expired)
            query = query.Where(b => b.Expiry < today && b.Available > 0);
        if (filter.Days.HasValue)
        {
            var until = today.AddDays(RequireDays(filter.Days.Value));
            query = query.Where(b => b.Expiry >= today && b.Expiry <= until && b.Available > 0);
        }
        if (filter.Status.HasValue) query = query.Where(b => b.Status == filter.Status);
        if (filter.MedicineId.HasValue) query = query.Where(b => b.MedicineId == filter.MedicineId);
        if (filter.From.HasValue) query = query.Where(b => b.Expiry >= filter.From);
        if (filter.To.HasValue) query = query.Where(b => b.Expiry <= filter.To);
        if (Clean(filter.Search) is { } search)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(b => b.Number.Contains(term) || b.Medicine.Code.Contains(term) || b.Medicine.GenericName.ToUpper().Contains(term));
        }
        return await PageAsync(query.OrderBy(b => b.Expiry).ThenBy(b => b.Id), filter, ct);
    }

    public async Task<IReadOnlyDictionary<PharmacyUnit, decimal>> ExpiredTotalsAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var today = Today;
        var query = db.MedicineBatches.AsNoTracking().Where(b => b.Expiry < today && b.Available > 0 &&
            (!filter.MedicineId.HasValue || b.MedicineId == filter.MedicineId) &&
            (!filter.Status.HasValue || b.Status == filter.Status) &&
            (!filter.From.HasValue || b.Expiry >= filter.From) && (!filter.To.HasValue || b.Expiry <= filter.To));
        if (Clean(filter.Search) is { } search)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(b => b.Number.Contains(term) || b.Medicine.Code.Contains(term) || b.Medicine.GenericName.ToUpper().Contains(term));
        }
        return await query.GroupBy(b => b.Unit).Select(group => new { Unit = group.Key, Total = group.Sum(b => b.Available) })
            .ToDictionaryAsync(group => group.Unit, group => group.Total, ct);
    }

    public async Task<PharmacyPage<PharmacyStockMovement>> MovementsAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        return await PageAsync(Movements(filter).Include(m => m.Batch).ThenInclude(b => b.Medicine)
            .OrderByDescending(m => m.OccurredAtUtc).ThenBy(m => m.Id), filter, ct);
    }

    public async Task<IReadOnlyList<ConsumptionRow>> ConsumptionAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        return await Movements(filter).Where(m => m.Kind == StockMovementKind.Dispensed)
            .GroupBy(m => new { m.Batch.Medicine.Code, m.Batch.Medicine.GenericName, m.Unit })
            .OrderBy(g => g.Key.Code)
            .Select(g => new ConsumptionRow(g.Key.Code, g.Key.GenericName, g.Key.Unit, g.Sum(m => m.Quantity)))
            .ToArrayAsync(ct);
    }

    public async Task<PharmacyDashboard> DashboardAsync(ClaimsPrincipal user, int? days, CancellationToken ct)
    {
        await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var warning = RequireDays(days ?? WarningDays[0]);
        var today = Today;
        var until = today.AddDays(warning);
        var stock = Stock();
        var active = await stock.CountAsync(x => x.Medicine.IsActive, ct);
        var low = await stock.CountAsync(x => x.Medicine.IsActive && (x.Eligible == 0 || x.Eligible < x.Medicine.ReorderThreshold), ct);
        var expired = await db.MedicineBatches.CountAsync(b => b.Expiry < today && b.Available > 0, ct);
        var expiring = await db.MedicineBatches.CountAsync(b => b.Expiry >= today && b.Expiry <= until && b.Available > 0, ct);
        var work = PrescriptionRows(KnownPrescriptions(), await HeldRevisionIdsAsync(ct));
        var pending = await work.CountAsync(x => x.Status == "Outstanding", ct);
        var partial = await work.CountAsync(x => x.Status == "Partial", ct);
        var completed = await db.PrescriptionDispenses.CountAsync(d => !d.IsPartial, ct);
        var recent = await db.PharmacyStockMovements.AsNoTracking().Include(x => x.Batch).ThenInclude(x => x.Medicine)
            .OrderByDescending(x => x.OccurredAtUtc).Take(8).ToArrayAsync(ct);
        return new(active, low, expired, expiring, pending, partial, completed, recent,
            await stock.Where(x => x.Medicine.IsActive).OrderBy(x => x.Medicine.GenericName).Take(10).ToArrayAsync(ct), warning);
    }

    public async Task<PharmacyPage<PrescriptionRow>> PrescriptionsAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var query = PrescriptionRows(KnownPrescriptions(), await HeldRevisionIdsAsync(ct));
        if (Guid.TryParse(filter.Search?.Trim(), out var id)) query = query.Where(row => row.Id == id);
        else if (Clean(filter.Search) is { } term) query = query.Where(row => row.PatientNumber == term);
        if (Clean(filter.DispensingStatus) is { } status) query = query.Where(row => row.Status == status);
        var page = await PageAsync(query.OrderByDescending(row => row.SignedUtc).ThenBy(row => row.Id), filter, ct);
        await audit.WriteAsync("PharmacyPrescriptionQueueViewed", actor, null, "Pharmacy prescription queue", ct);
        return page;
    }

    public async Task<DispensingPage> PrescriptionAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var record = await LoadPrescriptionAsync(actor, id, ct);
        var history = await db.PrescriptionDispenses.AsNoTracking().Where(d => d.PrescriptionId == id)
            .Include(d => d.Lines).OrderByDescending(d => d.OccurredAtUtc).ToArrayAsync(ct);
        var eligible = !await SupersededAsync(id, ct) && !await HasPriorDispensingAsync(record, ct) && record.Items.Any(i => i.Quantity.HasValue && i.DispenseUnit.HasValue);
        var candidates = eligible ? await CandidateBatchesAsync(record.Items, ct) : [];
        var choices = record.Items.OrderBy(item => item.Position).Select(item => new DispenseChoice(item,
            (item.Quantity ?? 0) - history.SelectMany(d => d.Lines).Where(line => line.PrescriptionItemId == item.Id).Sum(line => line.Quantity),
            candidates.Where(batch => Matches(item, batch.Medicine)).ToArray())).ToArray();
        var status = !eligible ? "On hold" : !choices.Any(line => line.Remaining > 0) && history.Length > 0 ? "Complete" :
            history.Length > 0 ? "Partial" : "Outstanding";
        await audit.WriteAsync("PharmacyPrescriptionViewed", actor, null, $"Prescription {id:D}", ct);
        return new(Row(record, status), choices, history, eligible, Guid.NewGuid());
    }

    public async Task<Guid> DispenseAsync(ClaimsPrincipal user, Guid id, DispenseInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        Validate(input);
        RequireKey(input.RequestKey);
        var lines = input.Lines.Where(l => l.BatchId != Guid.Empty || l.Quantity != 0 || Clean(l.FefoOverrideReason) is not null).ToArray();
        if (lines.Length is < 1 or > 60 || lines.GroupBy(l => (l.ItemId, l.BatchId)).Any(g => g.Count() != 1))
            throw new PharmacyException("Enter at least one stock line and use each prescription item and batch combination once.");
        foreach (var line in lines) Validate(line);
        await using var transaction = await BeginAsync(ct);
        await LockRequestAsync(actor, input.RequestKey, ct);

        var record = await LoadPrescriptionAsync(actor, id, ct);
        if (!Same(record.Encounter.Patient.PatientNumber, input.PatientNumber) || Clean(input.PatientNumber) is null)
            throw new PharmacyException("Confirm the patient number for the selected prescription.");
        await LockEncounterAsync(record.EncounterId, ct);
        record = await LoadPrescriptionAsync(actor, id, ct);
        var prior = await db.PrescriptionDispenses.AsNoTracking().Include(d => d.Lines)
            .SingleOrDefaultAsync(d => d.ActorId == actor && d.RequestKey == input.RequestKey, ct);
        if (prior is not null)
        {
            if (prior.PrescriptionId == id && prior.Lines.Count == lines.Length && lines.All(line => prior.Lines.Any(p =>
                p.PrescriptionItemId == line.ItemId && p.BatchId == line.BatchId && p.Quantity == line.Quantity &&
                p.FefoOverrideReason == Clean(line.FefoOverrideReason))))
                return prior.Id;
            throw new PharmacyException("This dispensing key was already used for a different request.");
        }
        if (await SupersededAsync(id, ct) || await HasPriorDispensingAsync(record, ct))
            throw new PharmacyException("This prescription is superseded, voided, or requires reconciliation of an earlier dispensing.");

        var batchIds = lines.Select(l => l.BatchId).Distinct().ToArray();
        var selectedMedicines = await db.MedicineBatches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
            .Select(b => b.MedicineId).Distinct().ToArrayAsync(ct);
        foreach (var medicine in selectedMedicines.Order()) await LockMedicineAsync(medicine, ct);
        var today = Today;
        var batches = await db.MedicineBatches.Include(b => b.Medicine)
            .Where(b => selectedMedicines.Contains(b.MedicineId) && b.Status == BatchStatus.Available && b.Expiry >= today &&
                b.Available > 0 && b.Medicine.IsActive).OrderBy(b => b.Expiry).ThenBy(b => b.Id).ToArrayAsync(ct);

        var prescriptionItems = record.Items.ToDictionary(i => i.Id);
        var dispensed = await db.PrescriptionDispenseItems.AsNoTracking().Where(l => l.PrescriptionId == id)
            .GroupBy(l => l.PrescriptionItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(row => row.Id, row => row.Quantity, ct);

        var receipt = new PrescriptionDispense
        {
            Id = Guid.CreateVersion7(), PrescriptionId = id, ActorId = actor, RequestKey = input.RequestKey, OccurredAtUtc = Now
        };
        receipt.Number = $"DSP-{receipt.Id:N}";
        var balances = batches.ToDictionary(b => b.Id, b => b.Available);
        foreach (var inputLine in lines.OrderBy(l => batches.SingleOrDefault(b => b.Id == l.BatchId)?.Expiry).ThenBy(l => l.BatchId))
        {
            if (!prescriptionItems.TryGetValue(inputLine.ItemId, out var item) || !item.Quantity.HasValue || !item.DispenseUnit.HasValue)
                throw new PharmacyException("Choose an item with an explicit prescribed quantity and stock unit.");
            var batch = batches.SingleOrDefault(b => b.Id == inputLine.BatchId);
            if (batch is null || !Matches(item, batch.Medicine) || item.DispenseUnit != batch.Unit)
                throw new PharmacyException("Choose an eligible batch matching the prescribed medicine, strength and stock unit.");
            Quantity(inputLine.Quantity, batch.Unit);
            var total = dispensed.GetValueOrDefault(item.Id) + inputLine.Quantity;
            if (total > item.Quantity.Value || inputLine.Quantity > balances[batch.Id])
                throw new PharmacyException("The quantity exceeds the remaining prescription amount or available batch stock.");
            var preferred = batches.FirstOrDefault(b => b.MedicineId == batch.MedicineId && balances[b.Id] > 0);
            if (preferred?.Id != batch.Id && Clean(inputLine.FefoOverrideReason) is null)
                throw new PharmacyException("Select the earliest-expiring eligible batch or enter a reason for the override.");

            dispensed[item.Id] = total;
            balances[batch.Id] -= inputLine.Quantity;
            receipt.Lines.Add(new PrescriptionDispenseItem
            {
                Id = Guid.CreateVersion7(), PrescriptionId = id, PrescriptionItemId = item.Id, BatchId = batch.Id,
                Quantity = inputLine.Quantity, FefoOverrideReason = Clean(inputLine.FefoOverrideReason)
            });
        }

        receipt.IsPartial = record.Items.Any(i => !i.Quantity.HasValue || !i.DispenseUnit.HasValue || dispensed.GetValueOrDefault(i.Id) < i.Quantity);
        db.PrescriptionDispenses.Add(receipt);
        foreach (var batch in batches.Where(b => balances[b.Id] != b.Available))
        {
            batch.Available = balances[batch.Id];
            Touch(batch, actor);
        }
        foreach (var line in receipt.Lines)
            Movement(batches.Single(b => b.Id == line.BatchId), line.Quantity, StockMovementKind.Dispensed, actor, line.Id, line.FefoOverrideReason, line.Id);
        await CommitAsync(transaction, receipt.IsPartial ? "PharmacyPartialDispense" : "PharmacyDispensed", actor,
            $"Dispense {receipt.Id:D} Prescription {id:D}", ct);
        return receipt.Id;
    }

    public async Task<PharmacyPage<PrescriptionDispense>> HistoryAsync(ClaimsPrincipal user, PharmacyFilter filter, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var (start, stop) = Dates(filter);
        var query = db.PrescriptionDispenses.AsNoTracking().Where(d => !start.HasValue || d.OccurredAtUtc >= start)
            .Where(d => !stop.HasValue || d.OccurredAtUtc < stop);
        if (Clean(filter.Search) is { } search) query = query.Where(d => d.Number.Contains(search.ToUpper()));
        if (filter.DispensingStatus == "Partial") query = query.Where(d => d.IsPartial);
        if (filter.DispensingStatus == "Complete") query = query.Where(d => !d.IsPartial);
        if (filter.BatchId.HasValue) query = query.Where(d => d.Lines.Any(l => l.BatchId == filter.BatchId));
        if (filter.MedicineId.HasValue) query = query.Where(d => d.Lines.Any(l => l.Batch.MedicineId == filter.MedicineId));
        var page = await PageAsync(query.OrderByDescending(d => d.OccurredAtUtc).ThenBy(d => d.Id), filter, ct);
        await audit.WriteAsync("PharmacyDispensingHistoryViewed", actor, null, "Dispensing history", ct);
        return page;
    }

    public async Task<PrescriptionDispense> ReceiptAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, HospitalRoles.Pharmacist, ct);
        var receipt = await db.PrescriptionDispenses.AsNoTracking().Include(d => d.Prescription).ThenInclude(r => r.Encounter)
            .ThenInclude(e => e.Patient).Include(d => d.Prescription).ThenInclude(r => r.Encounter).ThenInclude(e => e.Doctor)
            .Include(d => d.Lines).ThenInclude(l => l.Batch).ThenInclude(b => b.Medicine).SingleOrDefaultAsync(d => d.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Dispense", id, ct);
        await audit.WriteAsync("PharmacyReceiptViewed", actor, null, $"Dispense {id:D}", ct);
        return receipt;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> DoctorStatusesAsync(ClaimsPrincipal user, Guid[] ids, CancellationToken ct)
    {
        var (_, doctor) = await clinical.CurrentDoctorAsync(user, ct);
        var rows = await PrescriptionRows(db.ClinicalRecords.AsNoTracking().Where(r => r.Kind == ClinicalRecordKind.Prescription &&
                r.Status == ClinicalStatus.Finalized && ids.Contains(r.Id) && clinical.Permitted(doctor).Any(e => e.Id == r.EncounterId)), await HeldRevisionIdsAsync(ct))
            .ToArrayAsync(ct);
        return rows.ToDictionary(row => row.Id, row => row.Status);
    }

    private IQueryable<MedicineStock> Stock()
    {
        var today = Today;
        return db.Medicines.AsNoTracking().Select(m => new MedicineStock
        {
            Medicine = m,
            Eligible = m.IsActive ? m.Batches.Where(b => b.Status == BatchStatus.Available && b.Expiry >= today)
                .Sum(b => (decimal?)b.Available) ?? 0 : 0
        });
    }

    private IQueryable<PharmacyStockMovement> Movements(PharmacyFilter filter)
    {
        var (start, stop) = Dates(filter);
        return db.PharmacyStockMovements.AsNoTracking().Where(m => (!start.HasValue || m.OccurredAtUtc >= start) &&
            (!stop.HasValue || m.OccurredAtUtc < stop) && (!filter.MedicineId.HasValue || m.Batch.MedicineId == filter.MedicineId) &&
            (!filter.BatchId.HasValue || m.BatchId == filter.BatchId) && (!filter.Kind.HasValue || m.Kind == filter.Kind) &&
            (!filter.Status.HasValue || m.Batch.Status == filter.Status));
    }

    private IQueryable<ClinicalRecord> KnownPrescriptions()
        => db.ClinicalRecords.AsNoTracking().Where(r => r.Kind == ClinicalRecordKind.Prescription && r.Status == ClinicalStatus.Finalized &&
            r.Encounter.Patient.IsActive && r.Encounter.Doctor.IsActive && r.Encounter.Appointment != null &&
            r.Encounter.Appointment.PatientId == r.Encounter.PatientId && r.Encounter.Appointment.DoctorId == r.Encounter.DoctorId &&
            (r.Encounter.Appointment.Status == AppointmentStatus.CheckedIn || r.Encounter.Appointment.Status == AppointmentStatus.Completed));

    private IQueryable<PrescriptionRow> PrescriptionRows(IQueryable<ClinicalRecord> query, Guid[] held)
        => query.Select(r => new PrescriptionRow
        {
            Id = r.Id, PatientNumber = r.Encounter.Patient.PatientNumber,
            PatientName = r.Encounter.Patient.FirstName + " " + r.Encounter.Patient.LastName,
            Doctor = r.Encounter.Doctor.FirstName + " " + r.Encounter.Doctor.LastName,
            SignedUtc = r.FinalizedAtUtc!.Value,
            Status = held.Contains(r.Id) || db.ClinicalRecords.Any(n => n.PreviousRevisionId == r.Id && n.Status != ClinicalStatus.Draft) ? "On hold" :
                db.PrescriptionDispenses.Any(d => d.PrescriptionId == r.Id && !d.IsPartial) ? "Complete" :
                db.PrescriptionDispenses.Any(d => d.PrescriptionId == r.Id) ? "Partial" :
                !r.Items.Any(i => i.Quantity.HasValue && i.DispenseUnit.HasValue) ? "On hold" : "Outstanding"
        });

    private static PrescriptionRow Row(ClinicalRecord r, string status) => new()
    {
        Id = r.Id, PatientNumber = r.Encounter.Patient.PatientNumber,
        PatientName = $"{r.Encounter.Patient.FirstName} {r.Encounter.Patient.LastName}",
        Doctor = $"{r.Encounter.Doctor.FirstName} {r.Encounter.Doctor.LastName}",
        SignedUtc = r.FinalizedAtUtc!.Value, Status = status
    };

    private async Task<ClinicalRecord> LoadPrescriptionAsync(Guid actor, Guid id, CancellationToken ct)
        => await KnownPrescriptions().Include(r => r.Encounter).ThenInclude(e => e.Patient)
            .Include(r => r.Encounter).ThenInclude(e => e.Doctor).Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Prescription", id, ct);

    private Task<bool> SupersededAsync(Guid id, CancellationToken ct)
        => db.ClinicalRecords.AnyAsync(r => r.PreviousRevisionId == id && r.Status != ClinicalStatus.Draft, ct);

    private async Task<Guid[]> HeldRevisionIdsAsync(CancellationToken ct)
    {
        if (db.Database.IsRelational())
            return await db.Database.SqlQueryRaw<Guid>("""
                WITH RECURSIVE held AS (
                    SELECT r."Id" FROM clinical_records r
                    WHERE EXISTS (SELECT 1 FROM prescription_dispenses d WHERE d."PrescriptionId" = r."PreviousRevisionId")
                    UNION
                    SELECT r."Id" FROM clinical_records r JOIN held h ON r."PreviousRevisionId" = h."Id"
                )
                SELECT "Id" AS "Value" FROM held
                """).ToArrayAsync(ct);

        var roots = await db.PrescriptionDispenses.Select(d => d.PrescriptionId).Distinct().ToArrayAsync(ct);
        var revisions = await db.ClinicalRecords.Where(r => r.PreviousRevisionId.HasValue)
            .Select(r => new { r.Id, Previous = r.PreviousRevisionId!.Value }).ToArrayAsync(ct);
        var byParent = revisions.ToLookup(revision => revision.Previous, revision => revision.Id);
        var pending = new Queue<Guid>(roots);
        var held = new HashSet<Guid>();
        while (pending.TryDequeue(out var root))
            foreach (var revision in byParent[root])
                if (held.Add(revision))
                    pending.Enqueue(revision);
        return held.ToArray();
    }

    private async Task<bool> HasPriorDispensingAsync(ClinicalRecord record, CancellationToken ct)
    {
        var predecessor = record.PreviousRevisionId;
        while (predecessor.HasValue)
        {
            if (await db.PrescriptionDispenses.AnyAsync(d => d.PrescriptionId == predecessor, ct))
                return true;
            predecessor = await db.ClinicalRecords.AsNoTracking().Where(r => r.Id == predecessor).Select(r => r.PreviousRevisionId).SingleAsync(ct);
        }
        return false;
    }

    private async Task<MedicineBatch[]> CandidateBatchesAsync(ICollection<PrescriptionItem> items, CancellationToken ct)
    {
        var today = Today;
        var names = items.Select(i => i.Medicine.Trim().ToUpperInvariant()).Distinct().ToArray();
        return await db.MedicineBatches.AsNoTracking().Include(b => b.Medicine).Where(b => b.Status == BatchStatus.Available &&
            b.Available > 0 && b.Expiry >= today && b.Medicine.IsActive &&
            (names.Contains(b.Medicine.GenericName.ToUpper()) || b.Medicine.BrandName != null && names.Contains(b.Medicine.BrandName.ToUpper())))
            .OrderBy(b => b.Expiry).ThenBy(b => b.Id).ToArrayAsync(ct);
    }

    private static bool Matches(PrescriptionItem item, Medicine medicine)
        => item.DispenseUnit == medicine.Unit && (Same(item.Medicine, medicine.GenericName) || Same(item.Medicine, medicine.BrandName)) &&
            Same(item.Strength, medicine.Strength);

    private async Task<PharmacyPage<T>> PageAsync<T>(IQueryable<T> query, PharmacyFilter filter, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / PageSize)));
        return new(filter, await query.Skip((filter.Page - 1) * PageSize).Take(PageSize).ToArrayAsync(ct), total);
    }

    private static (DateTime? Start, DateTime? Stop) Dates(PharmacyFilter filter)
    {
        if (filter.From > filter.To || filter.From is { Year: < 2 or > 9998 } || filter.To is { Year: < 2 or > 9998 })
            throw new PharmacyException("Choose a valid date range.");
        return (filter.From.HasValue ? AppointmentService.ToUtc(filter.From.Value, TimeOnly.MinValue) : null,
            filter.To.HasValue ? AppointmentService.ToUtc(filter.To.Value.AddDays(1), TimeOnly.MinValue) : null);
    }

    private int RequireDays(int days) => WarningDays.Contains(days) ? days : throw new PharmacyException("Choose a configured expiry warning period.");

    private Task<Guid> CatalogActorAsync(ClaimsPrincipal user, CancellationToken ct)
        => ActorAsync(user, user.IsInRole(HospitalRoles.Pharmacist) ? HospitalRoles.Pharmacist : HospitalRoles.Administrator, ct);

    private async Task<Guid> ActorAsync(ClaimsPrincipal user, string role, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole(role) ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            !await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.IsEnabled, ct))
            throw new ClinicalAccessException();
        return actor;
    }

    private async Task<ClinicalAccessException> DeniedAsync(Guid actor, string type, Guid id, CancellationToken ct)
    {
        await audit.WriteAsync("PharmacyAccessDenied", actor, null, $"{type} {id:D}", ct);
        return new ClinicalAccessException();
    }

    private Task<PharmacyStockMovement?> FindMovementAsync(Guid actor, Guid request, CancellationToken ct)
        => db.PharmacyStockMovements.AsNoTracking().Include(m => m.Batch).SingleOrDefaultAsync(m => m.ActorId == actor && m.RequestKey == request, ct);

    private void Movement(MedicineBatch batch, decimal quantity, StockMovementKind kind, Guid actor, Guid key, string? reason = null, Guid? line = null)
        => db.PharmacyStockMovements.Add(new PharmacyStockMovement
        {
            Id = Guid.CreateVersion7(), BatchId = batch.Id, Unit = batch.Unit, Quantity = quantity, Kind = kind, ActorId = actor,
            RequestKey = key, OccurredAtUtc = Now, Reason = reason, DispenseItemId = line
        });

    private void Touch(MedicineBatch batch, Guid actor)
        => (batch.UpdatedAtUtc, batch.UpdatedByUserId, batch.ConcurrencyToken) = (Now, actor, Guid.CreateVersion7());

    private Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
        => db.Database.IsRelational() ? db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : db.Database.BeginTransactionAsync(ct);

    private Task LockRequestAsync(Guid actor, Guid request, CancellationToken ct)
        => LockAsync($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({'p'.ToString() + actor.ToString("N") + request.ToString("N")}, 0))) lock_request", ct);

    private Task LockMedicineAsync(Guid id, CancellationToken ct)
        => LockAsync($"SELECT 1 AS \"Value\" FROM medicines WHERE \"Id\" = {id} FOR UPDATE", ct);

    private Task LockEncounterAsync(Guid id, CancellationToken ct)
        => LockAsync($"SELECT 1 AS \"Value\" FROM clinical_encounters WHERE \"Id\" = {id} FOR UPDATE", ct);

    private async Task LockAsync(FormattableString sql, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return;
        try
        {
            await db.Database.SqlQuery<int>(sql).ToListAsync(ct);
        }
        catch (PostgresException error) when (error.SqlState is "40P01" or "40001")
        {
            throw new PharmacyException("Stock was busy. Reload the page and try again.");
        }
    }

    private async Task CommitAsync(IDbContextTransaction transaction, string action, Guid actor, string detail, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(action, actor, null, detail, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception error) when (error is DbUpdateConcurrencyException ||
            (error is DbUpdateException ? error.InnerException : error) is PostgresException { SqlState: "23505" or "23503" or "23514" or "40P01" or "40001" })
        {
            db.ChangeTracker.Clear();
            throw new PharmacyException("Stock or the prescription changed. Reload the page and try again.");
        }
    }

    private static void Validate(object input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), null, true))
            throw new PharmacyException("Check the required fields and field lengths.");
    }

    private static void Quantity(decimal value, PharmacyUnit unit, bool allowZero = false)
    {
        var precision = unit is PharmacyUnit.Gram or PharmacyUnit.Millilitre ? 3 : 0;
        if (!Enum.IsDefined(unit) || value < 0 || (!allowZero && value == 0) ||
            value >= 1000000000000000m || decimal.Round(value, precision) != value)
            throw new PharmacyException("Enter a valid quantity for the stock unit. Tablets, capsules and each require whole numbers.");
    }

    private static void CheckToken(Guid actual, Guid expected)
    {
        if (actual == Guid.Empty || actual != expected) throw new PharmacyException("The stock record changed. Reload the page.");
    }

    private static void RequireKey(Guid key)
    {
        if (key == Guid.Empty) throw new PharmacyException("Reload the form to start a new request.");
    }

    private static bool Same(string? left, string? right) => string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
