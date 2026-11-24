using HospitalManagementSystem.Web.Services.Appointments;
using System.Security.Claims;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Billing;

public sealed partial class BillingService
{
    public async Task<BillingEditor> EditorAsync(ClaimsPrincipal user, Guid? id, Guid? patientId, Guid? replacesId,
        InvoiceInput? input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        if (id.HasValue)
        {
            var invoice = await LoadInvoiceAsync(actor.Id, id.Value, ct, track: false);
            RequireDraft(invoice);
            input ??= new InvoiceInput
            {
                PatientId = invoice.PatientId, ReplacesId = invoice.ReplacesId, Token = invoice.Token, ServiceDate = invoice.ServiceDate,
                Lines = invoice.Items.Select(item => new InvoiceLineInput { ServiceId = item.Price.ServiceId,
                    Quantity = item.Quantity, SourceId = item.AppointmentId ?? item.LabItemId ?? item.DispenseItemId }).ToList()
            };
        }
        else
        {
            if (replacesId.HasValue)
            {
                var original = await LoadInvoiceAsync(actor.Id, replacesId.Value, ct, track: false);
                if (original.Status != InvoiceStatus.Voided)
                    throw new BillingException("Only a voided invoice can be replaced.");
                patientId = original.PatientId;
            }
            input ??= new InvoiceInput { PatientId = patientId.GetValueOrDefault(), ServiceDate = Today, ReplacesId = replacesId };
        }

        var patients = await db.Patients.AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Select(p => new BillingPatient(p.Id, p.PatientNumber, p.FirstName + " " + p.LastName)).ToArrayAsync(ct);
        var choices = await db.BillableServices.AsNoTracking().Where(s => s.Active && s.Category != BillingCategory.Admission)
            .OrderBy(s => s.Code).ToArrayAsync(ct);
        var sources = input.PatientId != Guid.Empty ? await SourcesAsync(input.PatientId, ct) : [];
        return new(id, input, patients, choices, sources);
    }

    public async Task<Guid> SaveDraftAsync(ClaimsPrincipal user, Guid? id, InvoiceInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        if (!ValidDate(input.ServiceDate) || input.ServiceDate > Today)
            throw new BillingException("Choose a valid service date through today.");
        var selected = input.Lines.Where(l => l.ServiceId != Guid.Empty).ToArray();
        if (selected.Length is < 1 or > 20)
            throw new BillingException("Select one to twenty services.");
        if (await db.Patients.AnyAsync(p => p.Id == input.PatientId && p.IsActive, ct) is false)
            throw new BillingException("Choose an active patient.");

        await using var transaction = await BeginAsync(ct);
        if (id.HasValue) await LockInvoiceAsync(id.Value, ct);
        var invoice = id.HasValue ? await LoadInvoiceAsync(actor.Id, id.Value, ct) :
            new PatientInvoice { Id = Guid.CreateVersion7(), CreatedAtUtc = Now, CreatedByUserId = actor.Id };
        if (id.HasValue)
        {
            RequireDraft(invoice);
            CheckToken(input.Token, invoice.Token);
            if (invoice.PatientId != input.PatientId || invoice.ReplacesId != input.ReplacesId)
                throw new BillingException("A saved draft keeps its original patient and replacement link.");
        }
        if (input.ReplacesId.HasValue && (!await db.PatientInvoices.AnyAsync(i =>
                i.Id == input.ReplacesId && i.PatientId == input.PatientId && i.Status == InvoiceStatus.Voided, ct) ||
            await db.PatientInvoices.AnyAsync(i => i.ReplacesId == input.ReplacesId && i.Id != invoice.Id &&
                i.Status != InvoiceStatus.Voided, ct)))
            throw new BillingException("Select a voided invoice for this patient that has no active replacement.");

        var serviceIds = selected.Select(l => l.ServiceId).Distinct().ToArray();
        var prices = await db.BillableServices.AsNoTracking().Where(s => s.Active && serviceIds.Contains(s.Id))
            .Include(s => s.Prices.Where(p => p.From <= input.ServiceDate && (p.Until == null || p.Until > input.ServiceDate)))
            .ToDictionaryAsync(s => s.Id, ct);
        var sources = selected.Any(line => line.SourceId.HasValue) ? await SourcesAsync(input.PatientId, ct) : [];
        var items = new List<PatientInvoiceItem>(selected.Length);
        var sourceIds = new HashSet<(BillingCategory, Guid)>();
        var manualServices = new HashSet<Guid>();
        foreach (var entry in selected)
        {
            if (!prices.TryGetValue(entry.ServiceId, out var service) || service.Prices.Count != 1)
                throw new BillingException("Each active service needs one configured price on the service date.");
            if (entry.Quantity <= 0 || entry.Quantity >= 1000000000000000m || decimal.Round(entry.Quantity, 3) != entry.Quantity)
                throw new BillingException("Enter positive quantities with at most three decimal places.");
            var source = entry.SourceId.HasValue ? sources.SingleOrDefault(item =>
                item.Id == entry.SourceId && item.Category == service.Category) : null;
            ValidateSource(service, source, entry.SourceId, entry.Quantity, input.ServiceDate);
            if (entry.SourceId is Guid sourceId && !sourceIds.Add((service.Category, sourceId)) ||
                entry.SourceId is null && !manualServices.Add(service.Id))
                throw new BillingException("Combine repeated manual services or choose distinct clinical sources.");
            var price = service.Prices.Single();
            if (price.Amount > 0 && entry.Quantity >= 10000000000000000m / price.Amount)
                throw new BillingException("This line exceeds the supported LKR amount.");
            var total = decimal.Round(entry.Quantity * price.Amount, 2, MidpointRounding.AwayFromZero);
            Money(total, true);
            items.Add(new PatientInvoiceItem
            {
                Id = Guid.CreateVersion7(), InvoiceId = invoice.Id, PriceId = price.Id,
                Category = service.Category, Code = service.Code, Name = service.Name, Quantity = entry.Quantity,
                UnitPrice = price.Amount, Total = total,
                AppointmentId = service.Category == BillingCategory.Consultation ? entry.SourceId : null,
                LabItemId = service.Category == BillingCategory.Laboratory ? entry.SourceId : null,
                DispenseItemId = service.Category == BillingCategory.Pharmacy ? entry.SourceId : null
            });
        }

        var grandTotal = items.Sum(item => item.Total);
        Money(grandTotal, true);
        if (id.HasValue)
        {
            db.PatientInvoiceItems.RemoveRange(invoice.Items);
            invoice.Items.Clear();
            db.PatientInvoiceItems.AddRange(items);
        }
        invoice.Items = items;
        invoice.PatientId = input.PatientId;
        invoice.ReplacesId = input.ReplacesId;
        invoice.ServiceDate = input.ServiceDate;
        invoice.Total = grandTotal;
        Touch(invoice, actor.Id);
        if (!id.HasValue) db.PatientInvoices.Add(invoice);
        await CommitAsync(transaction, id.HasValue ? "BillingDraftUpdated" : "BillingDraftCreated", actor.Id,
            $"Invoice {invoice.Id:D} Draft LKR {invoice.Total:0.00}", ct);
        return invoice.Id;
    }

    public async Task IssueAsync(ClaimsPrincipal user, Guid id, Guid token, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        await using var transaction = await BeginAsync(ct);
        await LockInvoiceAsync(id, ct);
        var invoice = await LoadInvoiceAsync(actor.Id, id, ct);
        RequireDraft(invoice);
        CheckToken(token, invoice.Token);
        if (!invoice.Patient.IsActive || invoice.Items.Count == 0)
            throw new BillingException("An active patient and at least one charge are required.");

        var serviceIds = invoice.Items.Select(line => line.Price.ServiceId).Distinct().Order().ToArray();
        foreach (var serviceId in serviceIds) await LockServiceAsync(serviceId, ct);
        var services = await db.BillableServices.AsNoTracking().Where(s => serviceIds.Contains(s.Id) && s.Active)
            .ToDictionaryAsync(s => s.Id, ct);
        var sources = await SourcesAsync(invoice.PatientId, ct);
        foreach (var line in invoice.Items)
        {
            var sourceId = line.AppointmentId ?? line.LabItemId ?? line.DispenseItemId;
            var source = sources.SingleOrDefault(s => s.Id == sourceId && s.Category == line.Category);
            if (!services.TryGetValue(line.Price.ServiceId, out var service) || service.Category != line.Category ||
                line.Price.From > invoice.ServiceDate || line.Price.Until <= invoice.ServiceDate)
                throw new BillingException("A draft price or service is no longer eligible. Review the draft.");
            ValidateSource(service, source, sourceId, line.Quantity, invoice.ServiceDate);
            if (sourceId.HasValue && await db.PatientInvoiceItems.AnyAsync(other => other.Claimed &&
                (other.AppointmentId == sourceId || other.LabItemId == sourceId || other.DispenseItemId == sourceId), ct))
                throw new BillingException("A clinical source is already on an issued invoice.");
        }
        foreach (var line in invoice.Items)
            line.Claimed = line.AppointmentId.HasValue || line.LabItemId.HasValue || line.DispenseItemId.HasValue;
        invoice.Total = invoice.Items.Sum(item => item.Total);
        invoice.Number = $"INV-{invoice.Id:N}";
        invoice.Status = InvoiceStatus.Issued;
        invoice.PatientLabel = $"{invoice.Patient.FirstName} {invoice.Patient.LastName}";
        invoice.PatientReference = invoice.Patient.PatientNumber;
        invoice.IssuedAtUtc = Now;
        invoice.IssuedByUserId = actor.Id;
        Touch(invoice, actor.Id);
        await CommitAsync(transaction, "BillingInvoiceIssued", actor.Id, $"Invoice {id:D} Draft to Issued LKR {invoice.Total:0.00}", ct);
    }

    public async Task VoidAsync(ClaimsPrincipal user, Guid id, BillingTransitionInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var reason = Reason(input.Reason);
        await using var transaction = await BeginAsync(ct);
        await LockInvoiceAsync(id, ct);
        var invoice = await LoadInvoiceAsync(actor.Id, id, ct);
        if (invoice.Status == InvoiceStatus.Voided && invoice.VoidedByUserId == actor.Id && invoice.VoidReason == reason) return;
        CheckToken(input.Token, invoice.Token);
        if (invoice.Status != InvoiceStatus.Issued || invoice.Payments.Any(p => p.Reversal is null))
            throw new BillingException("Only issued invoices without an active payment can be voided.");
        (invoice.Status, invoice.VoidedAtUtc, invoice.VoidedByUserId, invoice.VoidReason) = (InvoiceStatus.Voided, Now, actor.Id, reason);
        foreach (var line in invoice.Items) line.Claimed = false;
        Touch(invoice, actor.Id);
        await CommitAsync(transaction, "BillingInvoiceVoided", actor.Id, $"Invoice {id:D} Issued to Voided", ct);
    }

    public async Task<InvoicePage> InvoiceAsync(ClaimsPrincipal user, Guid id, bool print, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var invoice = await LoadInvoiceAsync(actor.Id, id, ct, track: false);
        if (print && invoice.Status == InvoiceStatus.Draft)
            throw new BillingException("Issue the invoice before printing.");
        await audit.WriteAsync(print ? "BillingInvoicePrinted" : "BillingInvoiceViewed", actor.Id, null, $"Invoice {id:D}", ct);
        var paid = Paid(invoice);
        return new(invoice, paid, Balance(invoice), Guid.NewGuid());
    }

    private async Task<BillingSource[]> SourcesAsync(Guid patientId, CancellationToken ct)
    {
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.Completed &&
                !db.PatientInvoiceItems.Any(line => line.Claimed && line.AppointmentId == a.Id))
            .Select(a => new { a.Id, a.AppointmentNumber, a.StartAtUtc }).ToArrayAsync(ct);
        var labs = await db.LaboratoryOrders.AsNoTracking().Where(o => o.Encounter.PatientId == patientId && o.Status == LabOrderStatus.Completed)
            .SelectMany(o => o.Items.Select(i => new { o.Number, i.Id, i.Code, o.CreatedAtUtc }))
            .Where(i => !db.PatientInvoiceItems.Any(line => line.Claimed && line.LabItemId == i.Id))
            .ToArrayAsync(ct);
        var dispensing = await db.PrescriptionDispenses.AsNoTracking().Where(d => d.Prescription.Encounter.PatientId == patientId)
            .SelectMany(d => d.Lines.Select(line => new { d.Number, line.Id, line.Quantity, line.Batch.Medicine.Code, d.OccurredAtUtc }))
            .Where(i => !db.PatientInvoiceItems.Any(line => line.Claimed && line.DispenseItemId == i.Id))
            .ToArrayAsync(ct);
        return [
            .. appointments.Select(a => new BillingSource(a.Id, BillingCategory.Consultation, a.AppointmentNumber, null, 1, LocalDate(a.StartAtUtc))),
            .. labs.Select(i => new BillingSource(i.Id, BillingCategory.Laboratory, i.Number, i.Code, 1, LocalDate(i.CreatedAtUtc))),
            .. dispensing.Select(i => new BillingSource(i.Id, BillingCategory.Pharmacy, i.Number, i.Code, i.Quantity, LocalDate(i.OccurredAtUtc)))
        ];
    }

    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(AppointmentService.ToColombo(utc));

    private static void ValidateSource(BillableService service, BillingSource? source, Guid? id, decimal quantity, DateOnly date)
    {
        if (service.Category == BillingCategory.Other && !id.HasValue)
            return;
        if (source is null || source.Quantity != quantity || source.Code != service.SourceCode || source.ServiceDate != date ||
            service.Category == BillingCategory.Admission)
            throw new BillingException("Choose an eligible unbilled source for this patient, service, date and actual quantity.");
    }

    private async Task<PatientInvoice> LoadInvoiceAsync(Guid actor, Guid id, CancellationToken ct, bool track = true)
    {
        var query = track ? db.PatientInvoices.AsTracking() : db.PatientInvoices.AsNoTracking();
        return await query.Include(i => i.Patient).Include(i => i.Items).ThenInclude(item => item.Price)
            .Include(i => i.Payments).ThenInclude(p => p.Reversal).AsSplitQuery().SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw await DeniedAsync(actor, "Invoice", id, ct);
    }

    private static void RequireDraft(PatientInvoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft) throw new BillingException("Only draft invoices can be changed or issued.");
    }
    private void Touch(PatientInvoice invoice, Guid actor)
        => (invoice.Token, invoice.UpdatedAtUtc, invoice.UpdatedByUserId) = (Guid.CreateVersion7(), Now, actor);
    private static decimal Paid(PatientInvoice invoice) => invoice.Payments.Where(p => p.Reversal is null).Sum(p => p.Amount);
    private static decimal Balance(PatientInvoice invoice) => invoice.Status == InvoiceStatus.Voided ? 0 : invoice.Total - Paid(invoice);
}
