using System.Globalization;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Reporting;

public sealed partial class HospitalReportingService
{
    private async Task<HospitalReport> BillingReport(ReportFilter f, DateTime from, DateTime to, bool dashboard, CancellationToken ct)
    {
        var category = Status<BillingCategory>(f);
        var invoices = db.PatientInvoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Issued);
        if (category.HasValue)
            invoices = invoices.Where(i => i.Items.Any(line => line.Category == category));
        var issued = invoices.Where(i => i.IssuedAtUtc >= from && i.IssuedAtUtc < to);
        var balances = issued.Select(i => new { i.Id, i.Number, i.IssuedAtUtc, i.Total, Paid = i.Payments.Where(p => p.Reversal == null).Sum(p => p.Amount) });
        var totals = await balances.GroupBy(_ => 1).Select(g => new {
            Issued = g.Sum(i => i.Total), Paid = g.Sum(i => i.Paid),
            Unpaid = g.Count(i => i.Paid == 0 && i.Total > 0),
            Partial = g.Count(i => i.Paid > 0 && i.Paid < i.Total),
            Full = g.Count(i => i.Paid >= i.Total)
        }).SingleOrDefaultAsync(ct);
        var payments = db.PatientPayments.AsNoTracking().Where(p => p.Invoice.Status == InvoiceStatus.Issued &&
            p.Reversal == null && p.RecordedAtUtc >= from && p.RecordedAtUtc < to);
        if (category.HasValue)
            payments = payments.Where(p => p.Invoice.Items.Any(line => line.Category == category));
        var methods = await payments.GroupBy(p => p.Method).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var reversalPayments = db.PatientPayments.AsNoTracking().Where(p => p.Reversal != null &&
            p.Reversal.CreatedAtUtc >= from && p.Reversal.CreatedAtUtc < to);
        if (category.HasValue) reversalPayments = reversalPayments.Where(p => p.Invoice.Items.Any(line => line.Category == category));
        var groups = new List<ReportGroup> {
            Money("Billing and collections", Value("Issued invoice totals", totals?.Issued ?? 0),
                Value("Payments collected", methods.Sum(m => m.Amount)), Value("Outstanding on invoices issued in period",
                    (totals?.Issued ?? 0) - (totals?.Paid ?? 0)), Value("Payments reversed in period", await reversalPayments.SumAsync(p => p.Amount, ct))),
            Group("Payment status of invoices issued in period", Value("Unpaid invoices", totals?.Unpaid ?? 0),
                Value("Partially paid invoices", totals?.Partial ?? 0), Value("Fully paid invoices", totals?.Full ?? 0),
                Value("Reversal count", await reversalPayments.CountAsync(ct))),
            Series("Collections by payment method", methods.OrderBy(m => m.Key).Select(m => Value(m.Key.ToString(), m.Amount)), "LKR")
        };
        var days = await payments.GroupBy(p => p.RecordedAtUtc.AddMinutes(330).Date)
            .Select(g => new { Day = g.Key, Amount = g.Sum(p => p.Amount) }).OrderBy(g => g.Day).ToListAsync(ct);
        groups.Add(Series("Collections by local day", days.Select(d => Value(d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.Amount)), "LKR"));
        ReportTable? table = null;
        if (!dashboard)
        {
            groups.Add(Series("Collections by week starting Monday", days.GroupBy(d => d.Day.AddDays(-((int)d.Day.DayOfWeek + 6) % 7))
                .Select(g => Value(g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), g.Sum(d => d.Amount))), "LKR"));
            groups.Add(Series("Collections by month", days.GroupBy(d => d.Day.ToString("yyyy-MM", CultureInfo.InvariantCulture))
                .Select(g => Value(g.Key, g.Sum(d => d.Amount))), "LKR"));
            var lines = await issued.SelectMany(i => i.Items).Where(l => !category.HasValue || l.Category == category)
                .GroupBy(i => i.Category).Select(g => new { g.Key, Total = g.Sum(i => i.Total) }).ToListAsync(ct);
            groups.Add(Series("Issued line totals by service category", lines.OrderBy(l => l.Key).Select(l => Value(l.Key.ToString(), l.Total)), "LKR"));
            var ordered = f.Order == "oldest" ? balances.OrderBy(i => i.IssuedAtUtc) : balances.OrderByDescending(i => i.IssuedAtUtc);
            var rows = await Page(ordered.ThenBy(i => i.Id), f).ToArrayAsync(ct);
            table = Table("Issued invoices", ["Invoice number", "Issued (Colombo)", "Invoiced (LKR)", "Collected (LKR)", "Outstanding (LKR)", "Payment status"],
                rows.Select(i => new[] { i.Number ?? "", DateLabel(i.IssuedAtUtc!.Value),
                    i.Total.ToString("N2"), i.Paid.ToString("N2"), (i.Total - i.Paid).ToString("N2"),
                    i.Paid >= i.Total ? "Fully paid" : i.Paid > 0 ? "Partially paid" : "Unpaid" }).ToArray());
        }
        return new(ReportKind.Billing, "Invoice cohorts use issue date. Collections use payment date and exclude all currently reversed payments. Category selects whole matching invoices for cash and balance figures.", groups,
            [EnumChoice<BillingCategory>("Service category", f.Status), DateOrder(f)], table);
    }

    private async Task<HospitalReport> PharmacyReport(ReportFilter f, DateTime from, DateTime to, bool detailed, bool dashboard, CancellationToken ct)
    {
        var today = Today;
        var expiry = today.AddDays(f.ExpiryDays ?? 30);
        var medicines = db.Medicines.AsNoTracking().Where(m => m.IsActive && (!f.Item.HasValue || m.Id == f.Item));
        var batches = db.MedicineBatches.AsNoTracking().Where(b => b.Medicine.IsActive &&
            (!f.Item.HasValue || b.MedicineId == f.Item) && (!f.Batch.HasValue || b.Id == f.Batch));
        var stock = medicines.Select(m => new { m.Id, m.GenericName, m.Code, m.Unit, m.ReorderThreshold,
            Eligible = m.Batches.Where(b => b.Status == BatchStatus.Available && b.Expiry >= today && b.Unit == m.Unit &&
                (!f.Batch.HasValue || b.Id == f.Batch)).Sum(b => b.Available) });
        if (f.Batch.HasValue) stock = stock.Where(s => batches.Any(b => b.MedicineId == s.Id));
        var allLow = stock.Where(s => s.Eligible == 0 || s.Eligible < s.ReorderThreshold);
        var groups = new List<ReportGroup> { Group("Current pharmacy inventory",
            Value("Active medicines", await stock.CountAsync(ct)), Value("Low-stock medicines", await allLow.CountAsync(ct)),
            Value("Out-of-stock medicines", await stock.CountAsync(s => s.Eligible == 0, ct)),
            Value("Eligible batches expiring in interval", await batches.CountAsync(b => b.Status == BatchStatus.Available &&
                b.Expiry >= today && b.Expiry <= expiry && b.Available > 0, ct)),
            Value("Expired batches with stock", await batches.CountAsync(b => b.Expiry < today && b.Available > 0, ct))) };
        if (dashboard) return new(ReportKind.Pharmacy, "Current inventory and expiry window", groups, []);
        var conditions = new[] { "Low stock", "Out of stock", "Expiring", "Expired", "Quarantined", "Recalled" };
        if (!string.IsNullOrEmpty(f.Status) && !conditions.Contains(f.Status)) throw new ArgumentException("Choose a valid stock status.");
        var filteredBatches = f.Status switch
        {
            "Low stock" => batches.Where(b => allLow.Any(s => s.Id == b.MedicineId)),
            "Out of stock" => batches.Where(b => stock.Any(s => s.Id == b.MedicineId && s.Eligible == 0)),
            "Expiring" => batches.Where(b => b.Expiry >= today && b.Expiry <= expiry && b.Available > 0 && b.Status == BatchStatus.Available),
            "Expired" => batches.Where(b => b.Expiry < today && b.Available > 0),
            "Quarantined" => batches.Where(b => b.Status == BatchStatus.Quarantined),
            "Recalled" => batches.Where(b => b.Status == BatchStatus.Recalled),
            _ => batches
        };
        var quantities = await filteredBatches.GroupBy(b => b.Unit).Select(g => new { g.Key, Physical = g.Sum(b => b.Available),
            Eligible = g.Sum(b => b.Status == BatchStatus.Available && b.Expiry >= today && b.Unit == b.Medicine.Unit ? b.Available : 0m) }).ToListAsync(ct);
        groups.Add(Series("Physical stock by unit", quantities.Select(g => Value(g.Key.ToString(), g.Physical))));
        groups.Add(Series("Eligible stock by unit", quantities.Select(g => Value(g.Key.ToString(), g.Eligible))));
        var inventoryByMedicine = await filteredBatches.GroupBy(b => new { b.MedicineId, b.Medicine.GenericName, b.Medicine.Code, b.Unit })
            .Select(g => new { g.Key, Physical = g.Sum(b => b.Available),
                Eligible = g.Sum(b => b.Status == BatchStatus.Available && b.Expiry >= today && b.Unit == b.Medicine.Unit ? b.Available : 0m) })
            .OrderBy(g => g.Key.GenericName).ThenBy(g => g.Key.MedicineId).ThenBy(g => g.Key.Unit).Take(100).ToArrayAsync(ct);
        foreach (var unit in inventoryByMedicine.GroupBy(row => row.Key.Unit))
        {
            groups.Add(Series("Physical stock by medicine (up to 100 overall)", unit.Select(row =>
                Value(row.Key.GenericName + " (" + row.Key.Code + ")", row.Physical)), unit.Key.ToString()));
            groups.Add(Series("Eligible stock by medicine (up to 100 overall)", unit.Select(row =>
                Value(row.Key.GenericName + " (" + row.Key.Code + ")", row.Eligible)), unit.Key.ToString()));
        }
        var low = await allLow.OrderBy(s => s.Eligible).ThenBy(s => s.Id).Take(25).ToArrayAsync(ct);
        foreach (var unit in low.GroupBy(l => l.Unit))
            groups.Add(Series("Low stock (up to 25 overall)", unit.Select(s => Value(s.GenericName + " (" + s.Code + ")", s.Eligible)), unit.Key.ToString()));
        ReportTable? table = null;
        if (detailed)
        {
            var movements = db.PharmacyStockMovements.AsNoTracking().Where(m => m.OccurredAtUtc >= from && m.OccurredAtUtc < to &&
                (!f.Item.HasValue || m.Batch.MedicineId == f.Item) && (!f.Batch.HasValue || m.BatchId == f.Batch));
            if (!string.IsNullOrWhiteSpace(f.Search) && f.Search != "All movements")
            {
                if (!Enum.TryParse<StockMovementKind>(f.Search, true, out var kind) || !Enum.IsDefined(kind))
                    throw new ArgumentException("Choose a valid movement type.");
                movements = movements.Where(m => m.Kind == kind);
            }
            var movementGroups = await movements.GroupBy(m => new { m.Kind, m.Unit }).Select(g => new { g.Key, Total = g.Sum(m => m.Quantity) }).ToListAsync(ct);
            foreach (var unit in movementGroups.GroupBy(m => m.Key.Unit))
                groups.Add(Series("Stock ledger by movement type", unit.Select(m => Value(m.Key.Kind.ToString(), m.Total)), unit.Key.ToString()));
            var dispensing = await db.PrescriptionDispenses.AsNoTracking().Where(d => d.OccurredAtUtc >= from && d.OccurredAtUtc < to &&
                ((!f.Item.HasValue && !f.Batch.HasValue) || d.Lines.Any(l =>
                    (!f.Item.HasValue || l.Batch.MedicineId == f.Item) && (!f.Batch.HasValue || l.BatchId == f.Batch))))
                .GroupBy(d => d.IsPartial).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            groups.Add(Series("Dispensing events", dispensing.Select(g => Value(g.Key ? "Partial" : "Complete", g.Count))));
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var events = await Page(movements.OrderByDescending(m => m.OccurredAtUtc).ThenBy(m => m.Id), f)
                    .Select(m => new { m.Batch.Number, m.Batch.Medicine.GenericName, m.Kind, m.Quantity, m.Unit, m.OccurredAtUtc }).ToArrayAsync(ct);
                table = Table("Stock movement history", ["Medicine", "Batch", "Movement", "Quantity", "Unit", "Occurred (Colombo)"],
                    events.Select(m => new[] { m.GenericName, m.Number, m.Kind.ToString(), Number(m.Quantity), m.Unit.ToString(), DateLabel(m.OccurredAtUtc) }).ToArray());
            }
        }
        if (table == null)
        {
            var rows = await Page(filteredBatches.OrderBy(b => b.Medicine.GenericName).ThenBy(b => b.Expiry).ThenBy(b => b.Id), f)
                .Select(b => new { b.Number, b.Medicine.GenericName, b.Unit, b.Available, b.Expiry, b.Status, CatalogUnit = b.Medicine.Unit }).ToArrayAsync(ct);
            table = Table("Batch inventory", ["Medicine", "Batch", "Unit", "Physical", "Eligible", "Expiry", "Status"],
                rows.Select(b => new[] { b.GenericName, b.Number, b.Unit.ToString(), Number(b.Available),
                    Number(b.Status == BatchStatus.Available && b.Expiry >= today && b.Unit == b.CatalogUnit ? b.Available : 0m),
                    b.Expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), b.Status.ToString() }).ToArray());
        }
        var catalog = await db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.GenericName).ThenBy(m => m.Id).Select(m => new { m.Id, m.GenericName, m.Code }).Take(250).ToArrayAsync(ct);
        var choices = new List<ReportChoice> {
            new("item", "Medicine", f.Item?.ToString() ?? "", catalog.Select(m => (m.Id.ToString(), m.GenericName + " (" + m.Code + ")")).ToArray()),
            new("batch", "Batch ID", f.Batch?.ToString() ?? ""), new("status", "Stock status", f.Status ?? "", conditions.Select(x => (x, x)).ToArray()),
            new("expiryDays", "Expiry window (days)", (f.ExpiryDays ?? 30).ToString(CultureInfo.InvariantCulture))
        };
        if (detailed) choices.Add(new("search", "Movement type (opens ledger)", f.Search ?? "",
            Enum.GetNames<StockMovementKind>().Prepend("All movements").Select(x => (x, x)).ToArray()));
        return new(ReportKind.Pharmacy, "Inventory is current. Ledger and dispensing use occurrence date. Eligible stock excludes expired, quarantined, recalled and incompatible batches.", groups, choices, table);
    }

    private async Task<HospitalReport> LaboratoryReport(ReportFilter f, DateTime from, DateTime to, bool dashboard, CancellationToken ct)
    {
        var orders = db.LaboratoryOrders.AsNoTracking().Where(o => o.CreatedAtUtc >= from && o.CreatedAtUtc < to);
        if (f.Department.HasValue) orders = orders.Where(o => o.Encounter.Doctor.DepartmentId == f.Department);
        if (f.Doctor.HasValue) orders = orders.Where(o => o.Encounter.DoctorId == f.Doctor);
        if (f.Item.HasValue) orders = orders.Where(o => o.Items.Any(i => i.TestId == f.Item));
        if (Status<LabOrderStatus>(f) is { } status) orders = orders.Where(o => o.Status == status);
        var counts = await orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var groups = new List<ReportGroup> {
            Group("Laboratory orders created in period", Value("Total orders", counts.Sum(g => g.Count)),
                Value("Pending laboratory requests", counts.Where(g => g.Key is not (LabOrderStatus.Completed or LabOrderStatus.Cancelled)).Sum(g => g.Count)),
                Value("Completed orders", counts.Where(g => g.Key == LabOrderStatus.Completed).Sum(g => g.Count))),
            Series("Orders by status", counts.Select(g => Value(g.Key.ToString(), g.Count)))
        };
        if (dashboard) return new(ReportKind.Laboratory, "Order creation date", groups, []);
        var items = orders.SelectMany(o => o.Items).Where(i => !f.Item.HasValue || i.TestId == f.Item);
        var tests = await items.GroupBy(i => new { i.TestId, i.Code }).Select(g => new { g.Key.Code, Count = g.Select(i => i.OrderId).Distinct().Count() })
            .OrderByDescending(t => t.Count).ThenBy(t => t.Code).Take(100).ToListAsync(ct);
        var samples = items.SelectMany(i => i.Samples);
        var results = items.SelectMany(i => i.Results);
        var verified = results.Where(r => r.Status == LabResultStatus.Verified && r.Sample.Status != LabSampleStatus.Rejected);
        var timed = verified.Where(r => r.VerifiedAtUtc.HasValue && r.VerifiedAtUtc >= r.Sample.CreatedAtUtc);
        var hours = await timed.AverageAsync(r => (double?)(r.VerifiedAtUtc!.Value - r.Sample.CreatedAtUtc).TotalHours, ct);
        var workflow = new List<ReportValue> {
            Value("Sample records collected", await samples.CountAsync(ct)),
            Value("Rejected samples", await samples.CountAsync(s => s.Status == LabSampleStatus.Rejected, ct)),
            Value("Additional samples on test items", await items.SumAsync(i => i.Samples.Count > 1 ? i.Samples.Count - 1 : 0, ct)),
            Value("Draft result versions", await results.CountAsync(r => r.Status == LabResultStatus.Draft, ct)),
            Value("Results awaiting verification", await results.CountAsync(r => r.Status == LabResultStatus.Submitted, ct)),
            Value("Verified result versions", await verified.CountAsync(ct))
        };
        if (hours.HasValue) workflow.Add(Value("Mean sample creation to verification (hours)", Math.Round((decimal)hours.Value, 2)));
        groups.Add(Group("Test workflow in selected order cohort", workflow.ToArray()));
        groups.Add(Series("Orders by test code (up to 100)", tests.Select(t => Value(t.Code, t.Count))));
        groups.Add(await Trend("Orders created by day", orders.Select(o => o.CreatedAtUtc), false, ct));
        var catalog = await db.LaboratoryTests.AsNoTracking().OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Name }).Take(250).ToArrayAsync(ct);
        return new(ReportKind.Laboratory, "Order creation date. Workflow counts include sample and result records belonging to those orders.", groups,
            [new("item", "Test", f.Item?.ToString() ?? "", catalog.Select(t => (t.Id.ToString(), t.Name)).ToArray()),
            await Departments(f, ct), await Doctors(f, null, ct), EnumChoice<LabOrderStatus>("Order status", f.Status)]);
    }

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

