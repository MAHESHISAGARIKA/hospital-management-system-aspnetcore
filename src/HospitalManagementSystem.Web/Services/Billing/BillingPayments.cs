using System.Security.Claims;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Billing;

public sealed partial class BillingService
{
    public async Task<Guid> PayAsync(ClaimsPrincipal user, Guid id, PaymentInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        Validate(input);
        Money(input.Amount);
        if (input.RequestKey == Guid.Empty || !Enum.IsDefined(input.Method))
            throw new BillingException("Reload the payment form and select a valid payment method.");
        var reference = Clean(input.BankReference)?.ToUpperInvariant();
        if (input.Method == BillingPaymentMethod.BankTransfer ? reference is null or { Length: > 80 } : reference is not null)
            throw new BillingException("Use a transfer reference for bank transfers only. Enter no bank account details.");
        await using var transaction = await BeginAsync(ct);
        var lockKey = $"billing:{actor.Id:N}:{input.RequestKey:N}";
        await LockAsync($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS billing_request", ct);
        await LockInvoiceAsync(id, ct);
        var invoice = await LoadInvoiceAsync(actor.Id, id, ct);
        var existing = await db.PatientPayments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ActorId == actor.Id && p.RequestKey == input.RequestKey, ct);
        if (existing is not null)
        {
            if (existing.InvoiceId == id && existing.Amount == input.Amount && existing.Method == input.Method &&
                existing.BankReference == reference)
                return existing.Id;
            throw new BillingException("This payment request key was already used with different details.");
        }

        CheckToken(input.Token, invoice.Token);
        if (invoice.Status != InvoiceStatus.Issued || input.Amount > Balance(invoice))
            throw new BillingException("Record a payment only on an issued invoice and within its outstanding balance.");
        if (reference is not null && await db.PatientPayments.AnyAsync(p => p.BankReference == reference, ct))
            throw new BillingException("This transfer reference was already recorded.");

        var payment = new PatientPayment
        {
            Id = Guid.CreateVersion7(), InvoiceId = id, RequestKey = input.RequestKey, Amount = input.Amount,
            BalanceAfter = Balance(invoice) - input.Amount, Method = input.Method, BankReference = reference,
            ActorId = actor.Id, RecordedBy = actor.UserName ?? actor.Id.ToString("D"), RecordedAtUtc = Now
        };
        payment.ReceiptNumber = $"RCT-{payment.Id:N}";
        db.PatientPayments.Add(payment);
        Touch(invoice, actor.Id);
        await CommitAsync(transaction, "BillingPaymentRecorded", actor.Id, $"Invoice {id:D} Payment {payment.Id:D} LKR {input.Amount:0.00}", ct);
        return payment.Id;
    }

    public async Task<Guid> ReverseAsync(ClaimsPrincipal user, Guid paymentId, BillingTransitionInput input, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var reason = Reason(input.Reason);
        await using var transaction = await BeginAsync(ct);
        var invoiceId = await db.PatientPayments.AsNoTracking().Where(p => p.Id == paymentId)
            .Select(p => (Guid?)p.InvoiceId).SingleOrDefaultAsync(ct) ?? throw await DeniedAsync(actor.Id, "Payment", paymentId, ct);
        await LockInvoiceAsync(invoiceId, ct);
        var invoice = await LoadInvoiceAsync(actor.Id, invoiceId, ct);
        var payment = invoice.Payments.Single(p => p.Id == paymentId);
        if (payment.Reversal is { } prior)
        {
            if (prior.ActorId == actor.Id && prior.Reason == reason) return invoiceId;
            throw new BillingException("This payment has already been reversed.");
        }
        CheckToken(input.Token, invoice.Token);
        if (invoice.Status != InvoiceStatus.Issued)
            throw new BillingException("Reverse payments only on issued invoices.");
        var reversal = new PaymentReversal { Id = Guid.CreateVersion7(), PaymentId = paymentId,
            ActorId = actor.Id, CreatedAtUtc = Now, Reason = reason };
        db.PaymentReversals.Add(reversal);
        Touch(invoice, actor.Id);
        await CommitAsync(transaction, "BillingPaymentReversed", actor.Id,
            $"Invoice {invoiceId:D} Payment {paymentId:D} Reversal {reversal.Id:D} LKR {payment.Amount:0.00}", ct);
        return invoiceId;
    }

    public async Task<PatientPayment> ReceiptAsync(ClaimsPrincipal user, Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var result = await db.PatientPayments.AsNoTracking().Include(p => p.Invoice).Include(p => p.Reversal)
            .SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw await DeniedAsync(actor.Id, "Receipt", id, ct);
        await audit.WriteAsync("BillingReceiptViewed", actor.Id, null, $"Payment {id:D} Invoice {result.InvoiceId:D}", ct);
        return result;
    }

    public async Task<BillingPage<InvoiceSummary>> DirectoryAsync(ClaimsPrincipal user, BillingFilter filter, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var query = InvoiceScope(filter);
        var page = await PageAsync(filter.Oldest ? query.OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id) :
            query.OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id), filter, ct);
        await audit.WriteAsync("BillingDirectoryViewed", actor.Id, null, "Invoice directory", ct);
        return page;
    }

    public async Task<BillingDashboard> DashboardAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var invoices = InvoiceScope(new());
        var issued = invoices.Where(i => i.Status == InvoiceStatus.Issued);
        var (start, stop) = Window(Today, Today);
        var recent = await db.PatientPayments.AsNoTracking().Include(p => p.Invoice).Include(p => p.Reversal)
            .OrderByDescending(p => p.RecordedAtUtc).Take(8).ToArrayAsync(ct);
        var result = new BillingDashboard(
            await invoices.CountAsync(i => i.Status == InvoiceStatus.Draft, ct),
            await issued.CountAsync(ct), await issued.CountAsync(i => i.Paid == 0 && i.Total > 0, ct),
            await issued.CountAsync(i => i.Paid > 0 && i.Paid < i.Total, ct),
            await issued.CountAsync(i => i.Paid == i.Total, ct), await db.PaymentReversals.CountAsync(ct),
            await issued.SumAsync(i => (decimal?)(i.Total - i.Paid), ct) ?? 0,
            await db.PatientPayments.Where(p => p.Reversal == null && p.RecordedAtUtc >= start && p.RecordedAtUtc < stop)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0,
            await invoices.OrderByDescending(i => i.CreatedAtUtc).Take(8).ToArrayAsync(ct), recent);
        await audit.WriteAsync("BillingDashboardViewed", actor.Id, null, "Billing dashboard", ct);
        return result;
    }

    public async Task<BillingReports> ReportsAsync(ClaimsPrincipal user, BillingFilter filter, CancellationToken ct)
    {
        var actor = await ActorAsync(user, ct);
        var (start, stop) = Window(filter.From ?? Today.AddDays(-29), filter.To ?? Today);
        var payments = db.PatientPayments.AsNoTracking().Where(p => !filter.Method.HasValue || p.Method == filter.Method)
            .Where(p => !filter.Category.HasValue || p.Invoice.Items.Any(i => i.Category == filter.Category));
        var receipts = payments.Where(p => p.RecordedAtUtc >= start && p.RecordedAtUtc < stop && p.Reversal == null);
        var daily = await DailyCollectionsAsync(receipts, start, stop, filter, ct);
        var methods = await receipts.GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount), Count = g.Count() }).ToArrayAsync(ct);
        var invoiced = await db.PatientInvoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued && i.IssuedAtUtc >= start && i.IssuedAtUtc < stop)
            .SelectMany(i => i.Items).Where(i => !filter.Category.HasValue || i.Category == filter.Category)
            .GroupBy(i => i.Category).Select(g => new { Category = g.Key, Amount = g.Sum(i => i.Total), Count = g.Count() }).ToArrayAsync(ct);

        var details = filter.Reversed
            ? payments.Where(p => p.Reversal != null && p.Reversal.CreatedAtUtc >= start && p.Reversal.CreatedAtUtc < stop)
                .OrderByDescending(p => p.Reversal!.CreatedAtUtc).ThenBy(p => p.Id)
            : payments.Where(p => p.RecordedAtUtc >= start && p.RecordedAtUtc < stop)
                .OrderByDescending(p => p.RecordedAtUtc).ThenBy(p => p.Id);
        var total = await details.CountAsync(ct);
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / PageSize)));
        var rows = await details.Skip((filter.Page - 1) * PageSize).Take(PageSize)
            .Include(p => p.Invoice).Include(p => p.Reversal).ToArrayAsync(ct);
        await audit.WriteAsync("BillingReportViewed", actor.Id, null, "Billing report", ct);
        return new(filter, daily,
            methods.Select(g => new BillingGroup(g.Method.ToString(), g.Amount, g.Count)).ToArray(),
            invoiced.Select(g => new BillingGroup(g.Category.ToString(), g.Amount, g.Count)).ToArray(), new(filter, rows, total));
    }

    private async Task<BillingGroup[]> DailyCollectionsAsync(IQueryable<PatientPayment> receipts,
        DateTime? start, DateTime? stop, BillingFilter filter, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var payments = await receipts.Select(p => new { p.RecordedAtUtc, p.Amount }).ToArrayAsync(ct);
            return payments.GroupBy(p => DateOnly.FromDateTime(AppointmentService.ToColombo(p.RecordedAtUtc)))
                .OrderByDescending(group => group.Key)
                .Select(group => new BillingGroup(group.Key.ToString("yyyy-MM-dd"), group.Sum(p => p.Amount), group.Count())).ToArray();
        }

        var method = filter.Method?.ToString();
        var category = filter.Category?.ToString();
        var rows = await db.Database.SqlQuery<DailyCollectionRow>($"""
            SELECT (p."RecordedAtUtc" AT TIME ZONE 'Asia/Colombo')::date AS "Day",
                   SUM(p."Amount") AS "Amount", COUNT(*)::integer AS "Count"
            FROM patient_payments p LEFT JOIN payment_reversals r ON r."PaymentId" = p."Id"
            WHERE r."Id" IS NULL AND p."RecordedAtUtc" >= {start} AND p."RecordedAtUtc" < {stop}
              AND ({method}::text IS NULL OR p."Method" = {method})
              AND ({category}::text IS NULL OR EXISTS (
                  SELECT 1 FROM patient_invoice_items i WHERE i."InvoiceId" = p."InvoiceId" AND i."Category" = {category}
              ))
            GROUP BY 1 ORDER BY 1 DESC
            """).ToArrayAsync(ct);
        return rows.Select(row => new BillingGroup(row.Day.ToString("yyyy-MM-dd"), row.Amount, row.Count)).ToArray();
    }

    private sealed class DailyCollectionRow
    {
        public DateOnly Day { get; set; }
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    private IQueryable<InvoiceSummary> InvoiceScope(BillingFilter filter)
    {
        var original = db.PatientInvoices.AsNoTracking();
        if (filter.Status.HasValue) original = original.Where(i => i.Status == filter.Status);
        if (filter.Category.HasValue) original = original.Where(i => i.Items.Any(item => item.Category == filter.Category));
        if (Clean(filter.Search) is { } search)
        {
            var term = search.ToUpperInvariant();
            original = original.Where(i => (i.Number != null && i.Number.ToUpper().Contains(term)) ||
                (i.PatientReference ?? i.Patient.PatientNumber).ToUpper() == term);
        }
        if (filter.From.HasValue || filter.To.HasValue)
        {
            var (start, stop) = Window(filter.From, filter.To);
            if (start.HasValue) original = original.Where(i => (i.IssuedAtUtc ?? i.CreatedAtUtc) >= start);
            if (stop.HasValue) original = original.Where(i => (i.IssuedAtUtc ?? i.CreatedAtUtc) < stop);
        }
        var query = original.Select(i => new InvoiceSummary
        {
            Id = i.Id, Number = i.Number, Patient = i.PatientLabel ?? (i.Patient.FirstName + " " + i.Patient.LastName),
            Reference = i.PatientReference ?? i.Patient.PatientNumber, Status = i.Status, CreatedAtUtc = i.CreatedAtUtc,
            IssuedAtUtc = i.IssuedAtUtc, Total = i.Total,
            Paid = i.Payments.Where(p => p.Reversal == null).Sum(p => (decimal?)p.Amount) ?? 0
        });
        if (filter.Outstanding) query = query.Where(i => i.Status == InvoiceStatus.Issued && i.Total > i.Paid);
        return filter.PaymentStatus switch
        {
            "Unpaid" => query.Where(i => i.Status == InvoiceStatus.Issued && i.Paid == 0 && i.Total > 0),
            "Partial" => query.Where(i => i.Status == InvoiceStatus.Issued && i.Paid > 0 && i.Paid < i.Total),
            "Paid" => query.Where(i => i.Status == InvoiceStatus.Issued && i.Paid == i.Total),
            _ => query
        };
    }

    private static (DateTime? Start, DateTime? Stop) Window(DateOnly? from, DateOnly? to)
    {
        if (from > to || from.HasValue && !ValidDate(from.Value) || to.HasValue && !ValidDate(to.Value))
            throw new BillingException("Choose a valid reporting date range.");
        return (from.HasValue ? AppointmentService.ToUtc(from.Value, TimeOnly.MinValue) : null,
            to.HasValue ? AppointmentService.ToUtc(to.Value.AddDays(1), TimeOnly.MinValue) : null);
    }
}
