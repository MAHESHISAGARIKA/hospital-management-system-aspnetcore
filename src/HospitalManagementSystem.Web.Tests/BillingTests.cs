using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Billing;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class BillingTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Fact]
    public async Task PricePeriodsValidateAndInvoiceUsesRoundedSnapshots()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing);
        var first = await billing.SaveServiceAsync(fixture.Actor, null, Service(fixture.Code), default);
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveServiceAsync(fixture.Actor, null, Service(fixture.Code.ToLowerInvariant()), default));
        var second = await billing.SaveServiceAsync(fixture.Actor, null, Service(fixture.Code + "B"), default);
        var date = billing.Today;
        var input = new InvoiceInput { PatientId = fixture.Patient, ServiceDate = date,
            Lines = [new() { ServiceId = first, Quantity = 1.005m }, new() { ServiceId = second }] };
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, input, default));
        await Assert.ThrowsAsync<BillingException>(() => billing.AddPriceAsync(fixture.Actor, first,
            new() { From = date, Until = date.AddDays(1), Amount = -1 }, default));
        await PriceAsync(billing, fixture.Actor, first, date.AddDays(-1), date, 2m);
        await PriceAsync(billing, fixture.Actor, first, date, date.AddDays(1), 1m);
        await PriceAsync(billing, fixture.Actor, second, date, date.AddDays(1), 4m);
        await Assert.ThrowsAsync<BillingException>(() => billing.AddPriceAsync(fixture.Actor, first,
            new() { From = date, Until = date.AddDays(2), Amount = 10 }, default));
        input.Lines[0].Quantity = 0;
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, input, default));
        input.Lines[0].Quantity = 1.005m;
        var id = await billing.SaveDraftAsync(fixture.Actor, null, input, default);
        var draft = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        Assert.Equal(5.01m, draft.Invoice.Total);
        input.Token = draft.Invoice.Token;
        input.Lines.RemoveAt(1);
        await billing.SaveDraftAsync(fixture.Actor, id, input, default);
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, id, input, default));
        draft = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        Assert.Equal(1.01m, Assert.Single(draft.Invoice.Items).Total);
        Assert.Equal(1m, draft.Invoice.Items[0].UnitPrice);
        await billing.IssueAsync(fixture.Actor, id, draft.Invoice.Token, default);
        await Assert.ThrowsAsync<BillingException>(() => billing.IssueAsync(fixture.Actor, id, draft.Invoice.Token, default));
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, id, input, default));
        await PriceAsync(billing, fixture.Actor, first, date.AddDays(1), date.AddDays(2), 8m);
        Assert.Equal(1.01m, (await billing.InvoiceAsync(fixture.Actor, id, true, default)).Invoice.Total);
    }

    [Fact]
    public async Task SourceOwnershipAndDuplicateClaimsAreValidatedAndVoidPermitsReplacement()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing, appointment: true);
        var unrelated = await SeedAsync(db, billing);
        var catalog = Service(fixture.Code);
        catalog.Category = BillingCategory.Consultation;
        var service = await billing.SaveServiceAsync(fixture.Actor, null, catalog, default);
        await PriceAsync(billing, fixture.Actor, service, billing.Today.AddDays(-1), billing.Today.AddDays(1), 10m);
        var input = new InvoiceInput { PatientId = unrelated.Patient, ServiceDate = billing.Today,
            Lines = [new() { ServiceId = service, SourceId = fixture.Visit }] };
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, input, default));
        input.PatientId = Guid.NewGuid();
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, input, default));
        input.PatientId = fixture.Patient;
        input.ServiceDate = billing.Today.AddDays(-1);
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, input, default));
        input.ServiceDate = billing.Today;
        var one = await billing.SaveDraftAsync(fixture.Actor, null, input, default);
        var two = await billing.SaveDraftAsync(fixture.Actor, null, input, default);
        await IssueAsync(billing, fixture, one);
        await Assert.ThrowsAsync<BillingException>(() => IssueAsync(billing, fixture, two));
        var issued = await billing.InvoiceAsync(fixture.Actor, one, false, default);
        await billing.VoidAsync(fixture.Actor, one, new() { Token = issued.Invoice.Token, Reason = "Synthetic correction" }, default);
        var original = await billing.InvoiceAsync(fixture.Actor, one, false, default);
        Assert.Equal(InvoiceStatus.Voided, original.Invoice.Status);
        Assert.Equal(issued.Invoice.Number, original.Invoice.Number);
        Assert.Equal(10m, Assert.Single(original.Invoice.Items).Total);
        input.ReplacesId = one;
        var replacement = await billing.SaveDraftAsync(fixture.Actor, null, input, default);
        await IssueAsync(billing, fixture, replacement);
        Assert.NotEqual(issued.Invoice.Number, (await billing.InvoiceAsync(fixture.Actor, replacement, false, default)).Invoice.Number);
    }

    [Fact]
    public async Task PaymentsReversalsReceiptsAndReportingReconcile()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing);
        var id = await DraftAsync(billing, fixture);
        var draft = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        await Assert.ThrowsAsync<BillingException>(() => billing.PayAsync(fixture.Actor, id,
            Pay(draft, 5), default));
        await IssueAsync(billing, fixture, id);
        var issued = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        var found = await billing.DirectoryAsync(fixture.Actor, new() { Search = issued.Invoice.Number?.ToUpperInvariant() }, default);
        Assert.Equal(id, Assert.Single(found.Rows).Id);
        var cash = Pay(issued, 4m);
        var first = await billing.PayAsync(fixture.Actor, id, cash, default);
        Assert.Equal(first, await billing.PayAsync(fixture.Actor, id, cash, default));
        cash.Amount = 3;
        await Assert.ThrowsAsync<BillingException>(() => billing.PayAsync(fixture.Actor, id, cash, default));
        var partial = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        Assert.Equal(6m, partial.Balance);
        Assert.Contains((await billing.DirectoryAsync(fixture.Actor, new() { PaymentStatus = "Partial" }, default)).Rows, row => row.Id == id);
        await Assert.ThrowsAsync<BillingException>(() => billing.VoidAsync(fixture.Actor, id, new() { Token = partial.Invoice.Token, Reason = "Synthetic" }, default));
        await Assert.ThrowsAsync<BillingException>(() => billing.PayAsync(fixture.Actor, id, Pay(partial, 7m), default));
        var bank = Pay(partial, 6m);
        bank.Method = BillingPaymentMethod.BankTransfer;
        await Assert.ThrowsAsync<BillingException>(() => billing.PayAsync(fixture.Actor, id, bank, default));
        bank.BankReference = $"Synthetic-{Guid.NewGuid():N}";
        var second = await billing.PayAsync(fixture.Actor, id, bank, default);
        var fullyPaid = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        Assert.Equal(0m, fullyPaid.Balance);
        var receipt = await billing.ReceiptAsync(fixture.Actor, second, default);
        Assert.Equal(6m, receipt.Amount);
        Assert.Equal(0m, receipt.BalanceAfter);
        Assert.NotEqual(receipt.ReceiptNumber, (await billing.ReceiptAsync(fixture.Actor, first, default)).ReceiptNumber);
        await Assert.ThrowsAsync<BillingException>(() => billing.ReverseAsync(fixture.Actor, second, new() { Token = fullyPaid.Invoice.Token }, default));
        var reversal = new BillingTransitionInput { Token = fullyPaid.Invoice.Token, Reason = "Synthetic reconciliation reason" };
        Assert.Equal(id, await billing.ReverseAsync(fixture.Actor, second, reversal, default));
        Assert.Equal(id, await billing.ReverseAsync(fixture.Actor, second, reversal, default));
        reversal.Reason = "Different synthetic reason";
        await Assert.ThrowsAsync<BillingException>(() => billing.ReverseAsync(fixture.Actor, second, reversal, default));
        Assert.Equal(6m, (await billing.InvoiceAsync(fixture.Actor, id, false, default)).Balance);
        var reversed = await billing.ReceiptAsync(fixture.Actor, second, default);
        Assert.NotNull(reversed.Reversal);
        Assert.Equal(receipt.ReceiptNumber, reversed.ReceiptNumber);
        var report = await billing.ReportsAsync(fixture.Actor, new() { Method = BillingPaymentMethod.BankTransfer, Reversed = true }, default);
        Assert.Contains(report.Payments.Rows, r => r.Id == second);
        Assert.DoesNotContain(report.Methods, r => r.Label == nameof(BillingPaymentMethod.BankTransfer) && r.Amount == 6m);
        var active = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        bank = Pay(active, 1m);
        bank.Method = BillingPaymentMethod.BankTransfer;
        bank.BankReference = reversed.BankReference!.ToLowerInvariant();
        await Assert.ThrowsAsync<BillingException>(() => billing.PayAsync(fixture.Actor, id, bank, default));
        Assert.DoesNotContain(await db.AuditLogEntries.ToArrayAsync(), log => log.Details?.Contains("Synthetic reconciliation reason") == true);
    }

    [Fact]
    public async Task LabAndPharmacyChargesUseApprovedMappingsAndSourceQuantities()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing, appointment: true);
        var visit = await db.Appointments.SingleAsync(p => p.Id == fixture.Visit);
        var encounter = new ClinicalEncounter { Id = Guid.NewGuid(), PatientId = fixture.Patient, DoctorId = visit.DoctorId };
        var catalogTest = new LaboratoryTest { Id = Guid.NewGuid(), Code = fixture.Code, Name = "Synthetic lab test", Specimen = "Synthetic" };
        var labItem = new LaboratoryOrderItem { Id = Guid.NewGuid(), TestId = catalogTest.Id, Code = fixture.Code, Name = "Synthetic lab item" };
        var order = new LaboratoryOrder { Id = Guid.NewGuid(), Number = "LAB-TEST", EncounterId = encounter.Id,
            CreatedAtUtc = DateTime.UtcNow, Items = [labItem], Status = LabOrderStatus.InProgress };
        var medicine = new Medicine { Id = Guid.NewGuid(), Code = fixture.Code, GenericName = "Synthetic medicine" };
        var batch = new MedicineBatch { Id = Guid.NewGuid(), MedicineId = medicine.Id, Number = "Synthetic batch" };
        var rxItem = new PrescriptionItem { Id = Guid.NewGuid(), Medicine = "Synthetic medicine", Instructions = "Private synthetic directions" };
        var rx = new ClinicalRecord { Id = Guid.NewGuid(), EncounterId = encounter.Id, Kind = ClinicalRecordKind.Prescription,
            Items = [rxItem], Status = ClinicalStatus.Finalized };
        var dispensedItem = new PrescriptionDispenseItem { Id = Guid.NewGuid(), PrescriptionId = rx.Id, PrescriptionItemId = rxItem.Id,
            BatchId = batch.Id, Quantity = 2.5m };
        var dispense = new PrescriptionDispense { Id = Guid.NewGuid(), Number = "DSP-TEST", PrescriptionId = rx.Id,
            OccurredAtUtc = DateTime.UtcNow, Lines = [dispensedItem] };
        db.AddRange(encounter, catalogTest, order, medicine, batch, rx, dispense);
        await db.SaveChangesAsync();

        var lab = Service(fixture.Code + "L");
        lab.Category = BillingCategory.Laboratory;
        lab.SourceCode = fixture.Code;
        var pharm = Service(fixture.Code + "P");
        pharm.Category = BillingCategory.Pharmacy;
        pharm.SourceCode = fixture.Code;
        var labService = await billing.SaveServiceAsync(fixture.Actor, null, lab, default);
        var pharmacyService = await billing.SaveServiceAsync(fixture.Actor, null, pharm, default);
        foreach (var id in new[] { labService, pharmacyService })
            await PriceAsync(billing, fixture.Actor, id, billing.Today, billing.Today.AddDays(1), 4m);
        var invoice = new InvoiceInput { PatientId = fixture.Patient, ServiceDate = billing.Today,
            Lines = [new() { ServiceId = labService, SourceId = labItem.Id },
                new() { ServiceId = pharmacyService, SourceId = dispensedItem.Id, Quantity = 2.5m }] };
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, invoice, default));
        order.Status = LabOrderStatus.Completed;
        await db.SaveChangesAsync();
        invoice.Lines[1].Quantity = 3;
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(fixture.Actor, null, invoice, default));
        invoice.Lines[1].Quantity = 2.5m;
        var invoiceId = await billing.SaveDraftAsync(fixture.Actor, null, invoice, default);
        await IssueAsync(billing, fixture, invoiceId);
        var issued = await billing.InvoiceAsync(fixture.Actor, invoiceId, false, default);
        Assert.Equal(14m, issued.Invoice.Total);
        Assert.All(issued.Invoice.Items, line => Assert.Equal("Fictional service", line.Name));
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Doctor)]
    [InlineData(HospitalRoles.Receptionist)]
    [InlineData(HospitalRoles.LaboratoryStaff)]
    [InlineData(HospitalRoles.Pharmacist)]
    [InlineData(HospitalRoles.Nurse)]
    public async Task NonAccountantsCannotOpenReceiptsOrPostPayments(string role)
    {
        using var client = Client();
        await SignInAsync(client, role);
        using var receipt = await client.GetAsync($"/Billing/Receipts/{Guid.NewGuid()}");
        Assert.Contains("/Account/AccessDenied", receipt.Headers.Location?.OriginalString);
        using var pay = await client.PostAsync($"/Billing/Payments/Create/{Guid.NewGuid()}", new FormUrlEncodedContent([]));
        Assert.Contains("/Account/AccessDenied", pay.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AccountantPagesAreNoStoreAndRenderPaymentAndReceipt()
    {
        using var client = Client();
        await SignInAsync(client, HospitalRoles.Accountant);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing);
        var invoice = await DraftAsync(billing, fixture);
        await IssueAsync(billing, fixture, invoice);
        var details = await billing.InvoiceAsync(fixture.Actor, invoice, false, default);
        var payment = await billing.PayAsync(fixture.Actor, invoice, Pay(details, 2m), default);
        foreach (var path in new[] { "/Billing", "/Billing/Services", "/Billing/Services/Create", "/Billing/Invoices", "/Billing/Outstanding",
            $"/Billing/Invoices/Create?patientId={fixture.Patient}", $"/Billing/Invoices/Details/{invoice}",
            $"/Billing/Invoices/Print/{invoice}", $"/Billing/Receipts/{payment}", "/Billing/Reports" })
        {
            using var page = await client.GetAsync(path);
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"{path} returned {page.StatusCode}.");
            Assert.True(page.Headers.CacheControl?.NoStore);
        }
        var printable = await client.GetStringAsync($"/Billing/Receipts/{payment}");
        Assert.Contains("LKR 2.00", printable);
        Assert.Contains("Balance immediately after payment", printable);
        Assert.DoesNotContain("DateOfBirth", printable);
        using var post = await client.PostAsync($"/Billing/Payments/Create/{invoice}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlSerializesBillingPayments()
    {
        var connectionString = await PostgreSqlTestDatabase.VerifiedConnectionStringAsync();
        var options = new DbContextOptionsBuilder<HospitalDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HospitalDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var billing = CreateService(db);
        var fixture = await SeedAsync(db, billing);
        var secondActor = (await SeedAsync(db, billing)).Actor;
        var id = await DraftAsync(billing, fixture);
        await IssueAsync(billing, fixture, id);
        var invoice = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        var request = Pay(invoice, 10m);
        async Task<Guid?> Record(ClaimsPrincipal actor, PaymentInput input)
        {
            await using var isolated = new HospitalDbContext(options);
            try { return await CreateService(isolated).PayAsync(actor, id, input, default); }
            catch (BillingException) { return null; }
        }
        var attempts = await Task.WhenAll(Record(fixture.Actor, request), Record(fixture.Actor, request), Record(secondActor, Pay(invoice, 10m)));
        Assert.Single(attempts.OfType<Guid>().Distinct());
        Assert.Equal(1, await db.PatientPayments.CountAsync(p => p.InvoiceId == id));
        db.ChangeTracker.Clear();
        var paid = await billing.InvoiceAsync(fixture.Actor, id, false, default);
        Assert.Equal(0m, paid.Balance);
        var payment = Assert.Single(paid.Invoice.Payments);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patient_payments SET \"Amount\" = {1m} WHERE \"Id\" = {payment.Id}"));
        Assert.Equal("23514", denied.SqlState);
        await billing.ReverseAsync(fixture.Actor, payment.Id, new() { Token = paid.Invoice.Token, Reason = "Synthetic test" }, default);
        Assert.Equal(10m, (await billing.InvoiceAsync(fixture.Actor, id, false, default)).Balance);
        Assert.Equal(payment.ReceiptNumber, (await billing.ReceiptAsync(fixture.Actor, payment.Id, default)).ReceiptNumber);

        var other = await SeedAsync(db, billing, appointment: true);
        var consultation = Service(other.Code);
        consultation.Category = BillingCategory.Consultation;
        var consultId = await billing.SaveServiceAsync(other.Actor, null, consultation, default);
        var today = billing.Today;
        await PriceAsync(billing, other.Actor, consultId, today, today.AddDays(2), 5m);
        var overlap = new BillingPrice { Id = Guid.NewGuid(), ServiceId = consultId, From = today.AddDays(1),
            Until = today.AddDays(3), Amount = 9m, CreatedAtUtc = DateTime.UtcNow };
        db.BillingPrices.Add(overlap);
        var overlapError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23P01", Assert.IsType<PostgresException>(overlapError.InnerException).SqlState);
        db.ChangeTracker.Clear();

        var charge = new InvoiceInput { PatientId = other.Patient, ServiceDate = today,
            Lines = [new() { ServiceId = consultId, SourceId = other.Visit }] };
        var firstInvoice = await billing.SaveDraftAsync(other.Actor, null, charge, default);
        var competingInvoice = await billing.SaveDraftAsync(other.Actor, null, charge, default);
        await IssueAsync(billing, other, firstInvoice);
        await Assert.ThrowsAsync<BillingException>(() => IssueAsync(billing, other, competingInvoice));
        Assert.Equal(InvoiceStatus.Draft, (await billing.InvoiceAsync(other.Actor, competingInvoice, false, default)).Invoice.Status);
        Assert.Equal(1, await db.PatientInvoiceItems.CountAsync(item => item.Claimed && item.AppointmentId == other.Visit));

        var invoiceNumber = (await billing.InvoiceAsync(other.Actor, firstInvoice, false, default)).Invoice.Number;
        var receiptNumber = payment.ReceiptNumber;
        var numbers = new PatientPayment
        {
            Id = Guid.NewGuid(), InvoiceId = firstInvoice, ReceiptNumber = receiptNumber, Amount = 1m,
            BalanceAfter = 4m, RequestKey = Guid.NewGuid(), RecordedBy = "Synthetic", RecordedAtUtc = DateTime.UtcNow
        };
        db.PatientPayments.Add(numbers);
        var receiptConflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(receiptConflict.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var numberConflict = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE patient_invoices SET \"Status\" = 'Issued', \"Number\" = {invoiceNumber}, \"IssuedAtUtc\" = {DateTime.UtcNow}, \"IssuedByUserId\" = {Guid.NewGuid()}, \"PatientLabel\" = 'Synthetic', \"PatientReference\" = 'Synthetic' WHERE \"Id\" = {competingInvoice}"));
        Assert.Equal("23505", numberConflict.SqlState);
        Assert.Empty(await db.PatientPayments.Where(p => p.InvoiceId == firstInvoice).ToArrayAsync());

    }

    private static BillingService CreateService(HospitalDbContext db)
        => new(db, new AuditLogService(db, new HttpContextAccessor()), TimeProvider.System);

    private static BillingCatalogInput Service(string code) => new() { Code = code, Name = "Fictional service", Category = BillingCategory.Other };
    private static Task PriceAsync(BillingService billing, ClaimsPrincipal actor, Guid id, DateOnly from, DateOnly until, decimal amount)
        => billing.AddPriceAsync(actor, id, new() { From = from, Until = until, Amount = amount }, default);

    private static async Task<Guid> DraftAsync(BillingService billing, Fixture fixture)
    {
        var service = await billing.SaveServiceAsync(fixture.Actor, null, Service(fixture.Code), default);
        await PriceAsync(billing, fixture.Actor, service, billing.Today, billing.Today.AddDays(1), 10m);
        return await billing.SaveDraftAsync(fixture.Actor, null, new()
        { PatientId = fixture.Patient, ServiceDate = billing.Today, Lines = [new() { ServiceId = service }] }, default);
    }

    private static async Task IssueAsync(BillingService billing, Fixture fixture, Guid id)
        => await billing.IssueAsync(fixture.Actor, id, (await billing.InvoiceAsync(fixture.Actor, id, false, default)).Invoice.Token, default);

    private static PaymentInput Pay(InvoicePage invoice, decimal amount)
        => new() { Token = invoice.Invoice.Token, RequestKey = Guid.NewGuid(), Amount = amount };

    private static async Task<Fixture> SeedAsync(HospitalDbContext db, BillingService billing, bool appointment = false)
    {
        var now = DateTime.UtcNow;
        var key = Guid.NewGuid().ToString("N");
        var email = $"{key}@example.test";
        var actor = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email,
            NormalizedUserName = email.ToUpperInvariant(), NormalizedEmail = email.ToUpperInvariant(),
            IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = key[..20], FirstName = "Fictional", LastName = "Patient",
            IsActive = true, Gender = "Unknown", DateOfBirth = new DateOnly(1990, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(actor, patient);
        Guid? visit = null;
        if (appointment)
        {
            var department = new Department { Id = Guid.NewGuid(), DepartmentCode = key[..10], Name = key, IsActive = true,
                CreatedAtUtc = now, UpdatedAtUtc = now };
            var doctor = new Doctor { Id = Guid.NewGuid(), DepartmentId = department.Id, DoctorNumber = key[..20],
                RegistrationNumber = key[..20], FirstName = "Fictional", LastName = "Doctor", Specialization = "Example",
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            var slot = new Appointment { Id = Guid.NewGuid(), AppointmentNumber = key[..20], IdempotencyKey = key,
                PatientId = patient.Id, DoctorId = doctor.Id, DepartmentId = department.Id, Status = AppointmentStatus.Completed,
                StartAtUtc = now, EndAtUtc = now.AddMinutes(15), CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedByUserId = actor.Id, UpdatedByUserId = actor.Id };
            db.AddRange(department, doctor, slot);
            visit = slot.Id;
        }
        await db.SaveChangesAsync();
        return new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()),
            new Claim(ClaimTypes.Role, HospitalRoles.Accountant)], "Tests")), patient.Id, key[..22].ToUpperInvariant(), visit);
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private async Task SignInAsync(HttpClient client, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var email = $"billing-{Guid.NewGuid():N}@example.test";
        var account = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, IsEnabled = true, EmailConfirmed = true };
        const string syntheticPassword = "Synthetic!Billing345";
        Assert.True((await users.CreateAsync(account, syntheticPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(account, role)).Succeeded);
        var html = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = email, ["Password"] = syntheticPassword, ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed record Fixture(ClaimsPrincipal Actor, Guid Patient, string Code, Guid? Visit);
}
