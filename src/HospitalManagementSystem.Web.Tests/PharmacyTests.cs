using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Pharmacy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class PharmacyTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Fact]
    public async Task CatalogStockAndAdjustmentsValidateUnitsAndKeepLedger()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var (pharmacy, clock) = Services(db);
        var fixture = await SeedAsync(db, pharmacy, clock);
        var medicine = await pharmacy.MedicineAsync(fixture.Pharmacist, fixture.MedicineId, default);
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.SaveMedicineAsync(fixture.Pharmacist, null, medicine, default));
        medicine.ReorderThreshold = 1.5m;
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.SaveMedicineAsync(fixture.Pharmacist, fixture.MedicineId, medicine, default));
        medicine.ReorderThreshold = 10;
        medicine.Description = "Synthetic update";
        await pharmacy.SaveMedicineAsync(fixture.Pharmacist, fixture.MedicineId, medicine, default);
        var receipt = Stock(fixture, 4, 3);
        var batch = await pharmacy.ReceiveAsync(fixture.Pharmacist, receipt, default);
        Assert.Equal(batch, await pharmacy.ReceiveAsync(fixture.Pharmacist, receipt, default));
        receipt.RequestKey = Guid.NewGuid();
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.ReceiveAsync(fixture.Pharmacist, receipt, default));
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, -1, 3), default));
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 1, -1), default));
        var stock = await pharmacy.BatchAsync(fixture.Pharmacist, batch, default);
        var input = new StockChangeInput { RequestKey = Guid.NewGuid(), Token = stock.ConcurrencyToken,
            Kind = StockMovementKind.Decrease, Quantity = 5, Reason = "Synthetic count" };
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.AdjustAsync(fixture.Pharmacist, batch, input, default));
        input.Quantity = 2;
        input.Reason = "";
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.AdjustAsync(fixture.Pharmacist, batch, input, default));
        input.Reason = "Synthetic count";
        await pharmacy.AdjustAsync(fixture.Pharmacist, batch, input, default);
        await pharmacy.AdjustAsync(fixture.Pharmacist, batch, input, default);
        input.RequestKey = Guid.NewGuid();
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.AdjustAsync(fixture.Pharmacist, batch, input, default));
        var movements = await pharmacy.MovementsAsync(fixture.Pharmacist, new() { BatchId = batch }, default);
        Assert.Equal(2, movements.Total);
        Assert.Equal(2, (await pharmacy.BatchAsync(fixture.Pharmacist, batch, default)).Available);
        Assert.Equal(2, Assert.Single((await pharmacy.MedicinesAsync(fixture.Pharmacist, new() { Search = fixture.MedicineCode, LowStock = true }, default)).Rows).Eligible);
        medicine = await pharmacy.MedicineAsync(fixture.Pharmacist, fixture.MedicineId, default);
        medicine.IsActive = false;
        await pharmacy.SaveMedicineAsync(fixture.Pharmacist, fixture.MedicineId, medicine, default);
        Assert.DoesNotContain(await pharmacy.ActiveMedicinesAsync(fixture.Pharmacist, default), m => m.Id == fixture.MedicineId);
        Assert.Equal(2, (await pharmacy.MovementsAsync(fixture.Pharmacist, new() { BatchId = batch }, default)).Total);
    }

    [Fact]
    public async Task DispensingEnforcesFefoPatientQuantityAndIdempotentPartialHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var (pharmacy, clock) = Services(db);
        var fixture = await SeedAsync(db, pharmacy, clock);
        var first = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 3, 1), default);
        var later = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 5, 3), default);
        var invalidCombined = Dispense(fixture, first, 3);
        invalidCombined.Lines.Add(new() { ItemId = fixture.Item, BatchId = later, Quantity = 3 });
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, invalidCombined, default));
        Assert.Equal(3, (await pharmacy.BatchAsync(fixture.Pharmacist, first, default)).Available);
        Assert.Equal(5, (await pharmacy.BatchAsync(fixture.Pharmacist, later, default)).Available);
        Assert.Empty(await db.PrescriptionDispenses.Where(d => d.PrescriptionId == fixture.Rx).ToArrayAsync());
        var input = Dispense(fixture, later, 2);
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, input, default));
        input.Lines[0].FefoOverrideReason = "Synthetic verification";
        input.PatientNumber = "incorrect";
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, input, default));
        input.PatientNumber = fixture.PatientNumber;
        var partial = await pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, input, default);
        Assert.Equal(partial, await pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, input, default));
        input.Lines[0].Quantity = 1;
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, input, default));
        var record = await pharmacy.PrescriptionAsync(fixture.Pharmacist, fixture.Rx, default);
        Assert.Equal("Partial", record.Prescription.Status);
        Assert.Equal(3, Assert.Single(record.Items).Remaining);
        Assert.Equal(first, record.Items.Single().Batches.First().Id);
        Assert.Equal("Partial", (await pharmacy.DoctorStatusesAsync(fixture.Doctor, [fixture.Rx], default))[fixture.Rx]);
        var excess = Dispense(fixture, first, 4);
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, excess, default));
        var complete = await pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, Dispense(fixture, first, 3), default);
        Assert.False((await pharmacy.ReceiptAsync(fixture.Pharmacist, complete, default)).IsPartial);
        Assert.Equal(0, (await pharmacy.BatchAsync(fixture.Pharmacist, first, default)).Available);
        Assert.Equal(2, (await pharmacy.HistoryAsync(fixture.Pharmacist, new() { MedicineId = fixture.MedicineId }, default)).Total);
        Assert.Equal(5, Assert.Single(await pharmacy.ConsumptionAsync(fixture.Pharmacist, new() { MedicineId = fixture.MedicineId }, default)).Quantity);
        Assert.DoesNotContain(await db.AuditLogEntries.ToArrayAsync(), a => a.Details?.Contains("Synthetic verification") == true);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => pharmacy.DispenseAsync(fixture.Doctor, fixture.Rx, input, default));
    }

    [Fact]
    public async Task ExpiryBoundaryQuarantineWrongMappingAndSupersededRecordsAreDenied()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var (pharmacy, clock) = Services(db);
        var fixture = await SeedAsync(db, pharmacy, clock);
        var expiresToday = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 5, 0), default);
        var quarantine = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 5, 31), default);
        var otherCode = $"OTHER-{Guid.NewGuid():N}"[..27].ToUpperInvariant();
        await pharmacy.SaveMedicineAsync(fixture.Pharmacist, null, Catalog(otherCode, "Different fictional medicine"), default);
        var otherMedicine = await db.Medicines.SingleAsync(m => m.Code == otherCode);
        var wrongMedicine = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture with { MedicineId = otherMedicine.Id }, 5, 5), default);
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx,
            Dispense(fixture, wrongMedicine, 1), default));
        var stock = await pharmacy.BatchAsync(fixture.Pharmacist, quarantine, default);
        await pharmacy.AdjustAsync(fixture.Pharmacist, quarantine, new()
        { Kind = StockMovementKind.Quarantined, Reason = "Synthetic quality hold", RequestKey = Guid.NewGuid(), Token = stock.ConcurrencyToken }, default);
        Assert.Contains((await pharmacy.InventoryAsync(fixture.Pharmacist, new() { Days = 30 }, default)).Rows, b => b.Id == expiresToday);
        Assert.Contains(Assert.Single((await pharmacy.PrescriptionAsync(fixture.Pharmacist, fixture.Rx, default)).Items).Batches, b => b.Id == expiresToday);
        clock.Utc = clock.Utc.AddDays(1);
        var inventory = await pharmacy.InventoryAsync(fixture.Pharmacist, new() { MedicineId = fixture.MedicineId, Expired = true }, default);
        Assert.Equal(expiresToday, Assert.Single(inventory.Rows).Id);
        Assert.Equal(0, Assert.Single((await pharmacy.MedicinesAsync(fixture.Pharmacist, new() { Search = fixture.MedicineCode }, default)).Rows).Eligible);
        foreach (var unavailable in new[] { expiresToday, quarantine })
            await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, Dispense(fixture, unavailable, 1), default));
        var rx = await db.ClinicalRecords.SingleAsync(r => r.Id == fixture.Rx);
        db.ClinicalRecords.Add(new ClinicalRecord { Id = Guid.NewGuid(), EncounterId = rx.EncounterId, Kind = rx.Kind,
            Status = ClinicalStatus.Voided, PreviousRevisionId = rx.Id, AmendmentReason = "Synthetic void" });
        await db.SaveChangesAsync();
        Assert.False((await pharmacy.PrescriptionAsync(fixture.Pharmacist, fixture.Rx, default)).CanDispense);
        await Assert.ThrowsAsync<PharmacyException>(() => pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, Dispense(fixture, expiresToday, 1), default));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => pharmacy.PrescriptionAsync(fixture.Pharmacist, Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Doctor)]
    [InlineData(HospitalRoles.Receptionist)]
    [InlineData(HospitalRoles.LaboratoryStaff)]
    [InlineData(HospitalRoles.Nurse)]
    [InlineData(HospitalRoles.Accountant)]
    public async Task NonPharmacistsCannotOpenDispensingOrModifyStock(string role)
    {
        using var client = Client();
        await SignInAsync(client, role);
        using var clinical = await client.GetAsync($"/Pharmacy/Prescriptions/Details/{Guid.NewGuid()}");
        Assert.Contains("/Account/AccessDenied", clinical.Headers.Location?.OriginalString);
        using var stock = await client.PostAsync("/Pharmacy/Stock/Receive", new FormUrlEncodedContent([]));
        Assert.Contains("/Account/AccessDenied", stock.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PharmacistDashboardLoadsAndWritesRequireAntiforgery()
    {
        using var client = Client();
        await SignInAsync(client, HospitalRoles.Pharmacist);
        foreach (var path in new[] { "/Pharmacy", "/Pharmacy/Medicines", "/Pharmacy/Inventory", "/Pharmacy/Prescriptions",
            "/Pharmacy/DispensingHistory", "/Pharmacy/Reports", "/Pharmacy/Reports/Consumption", "/Pharmacy/LowStock", "/Pharmacy/Expiry?Expired=true" })
        {
            using var page = await client.GetAsync(path);
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"{path} returned {page.StatusCode}.");
            Assert.True(page.Headers.CacheControl?.NoStore);
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var (pharmacy, clock) = Services(db);
        clock.Utc = DateTimeOffset.UtcNow;
        var fixture = await SeedAsync(db, pharmacy, clock);
        var batch = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 5, 7), default);
        var details = await client.GetStringAsync($"/Pharmacy/Prescriptions/Details/{fixture.Rx}");
        Assert.Contains("Confirm patient number", details);
        Assert.Contains("FEFO override reason", details);
        var receipt = await pharmacy.DispenseAsync(fixture.Pharmacist, fixture.Rx, Dispense(fixture, batch, 2), default);
        var printable = await client.GetStringAsync($"/Pharmacy/DispensingHistory/{receipt}");
        Assert.Contains("Print or save PDF", printable);
        Assert.Contains(fixture.PatientNumber, printable);
        Assert.Contains("Partial", printable);
        using var forbidden = await client.PostAsync($"/Pharmacy/Dispense/{Guid.NewGuid()}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlSerializesPharmacyStockAndPreservesLedger()
    {
        var connectionString = await PostgreSqlTestDatabase.VerifiedConnectionStringAsync();
        var options = new DbContextOptionsBuilder<HospitalDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HospitalDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var (pharmacy, clock) = Services(db);
        clock.Utc = DateTimeOffset.UtcNow;
        var fixture = await SeedAsync(db, pharmacy, clock);
        var competitor = await SeedAsync(db, pharmacy, clock, fixture.MedicineId);
        var batch = await pharmacy.ReceiveAsync(fixture.Pharmacist, Stock(fixture, 5, 7), default);
        var input = Dispense(fixture, batch, 5);
        var competitorInput = Dispense(competitor, batch, 5);
        async Task<Guid?> AttemptAsync(Fixture attempt, DispenseInput data)
        {
            await using var isolated = new HospitalDbContext(options);
            var (service, transactionClock) = Services(isolated);
            transactionClock.Utc = clock.Utc;
            try { return await service.DispenseAsync(attempt.Pharmacist, attempt.Rx, data, default); }
            catch (PharmacyException) { return null; }
        }
        var attempts = await Task.WhenAll(AttemptAsync(fixture, input), AttemptAsync(fixture, input), AttemptAsync(competitor, competitorInput));
        Assert.NotNull(attempts[0] ?? attempts[1] ?? attempts[2]);
        Assert.Single(attempts.OfType<Guid>().Distinct());
        Assert.Equal(0, (await pharmacy.BatchAsync(fixture.Pharmacist, batch, default)).Available);
        Assert.Equal(5, await db.PrescriptionDispenseItems.Where(i => i.BatchId == batch).SumAsync(i => i.Quantity));
        Assert.Equal(2, (await pharmacy.MovementsAsync(fixture.Pharmacist, new() { BatchId = batch }, default)).Total);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE medicine_batches SET \"Available\" = {-1m} WHERE \"Id\" = {batch}"));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(0, (await pharmacy.BatchAsync(fixture.Pharmacist, batch, default)).Available);
    }

    private static (PharmacyService Pharmacy, TestClock Clock) Services(HospitalDbContext db)
    {
        var audit = new AuditLogService(db, new HttpContextAccessor());
        var clock = new TestClock();
        return (new PharmacyService(db, new ClinicalRecordService(db, audit), audit, clock, Options.Create(new PharmacyOptions())), clock);
    }

    private static MedicineCatalogInput Catalog(string code, string name) => new() { Code = code, GenericName = name,
        Strength = "Synthetic strength", Form = "Synthetic form", Unit = PharmacyUnit.Tablet, ReorderThreshold = 3 };

    private static StockReceiptInput Stock(Fixture fixture, decimal quantity, int days) => new()
    { MedicineId = fixture.MedicineId, RequestKey = Guid.NewGuid(), Number = Guid.NewGuid().ToString("N"),
        ReceivedDate = fixture.Today, Expiry = fixture.Today.AddDays(days), Quantity = quantity };

    private static DispenseInput Dispense(Fixture fixture, Guid batch, decimal quantity) => new()
    { RequestKey = Guid.NewGuid(), PatientNumber = fixture.PatientNumber,
        Lines = [new() { ItemId = fixture.Item, BatchId = batch, Quantity = quantity }] };

    private static async Task<Fixture> SeedAsync(HospitalDbContext db, PharmacyService pharmacy, TestClock clock, Guid? sharedMedicine = null)
    {
        var now = clock.Utc.UtcDateTime;
        var suffix = Guid.NewGuid().ToString("N");
        ApplicationUser Account(string name)
        {
            var email = $"{name}-{suffix}@example.test";
            var account = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email,
                NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
                IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Users.Add(account);
            return account;
        }
        var pharm = Account("pharmacy");
        var clinician = Account("clinician");
        var department = new Department { Id = Guid.NewGuid(), DepartmentCode = suffix[..10], Name = suffix,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = suffix[..20], FirstName = "Fictional", LastName = "Patient",
            Gender = "Unknown", IsActive = true, DateOfBirth = new DateOnly(1990, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        var doctor = new Doctor { Id = Guid.NewGuid(), FirstName = "Fictional", LastName = "Doctor", Specialization = "Example",
            DoctorNumber = suffix[..20], RegistrationNumber = suffix[..20], DepartmentId = department.Id, ApplicationUserId = clinician.Id,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var appointment = new Appointment { Id = Guid.NewGuid(), AppointmentNumber = suffix[..20], IdempotencyKey = suffix,
            PatientId = patient.Id, DoctorId = doctor.Id, DepartmentId = department.Id, Status = AppointmentStatus.CheckedIn,
            StartAtUtc = now, EndAtUtc = now.AddMinutes(15), CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedByUserId = clinician.Id, UpdatedByUserId = clinician.Id };
        var encounter = new ClinicalEncounter { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            AppointmentId = appointment.Id, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = clinician.Id,
            UpdatedByUserId = clinician.Id, ConcurrencyToken = Guid.NewGuid() };
        var name = $"Fictional product {suffix}";
        if (sharedMedicine.HasValue)
            name = (await db.Medicines.SingleAsync(m => m.Id == sharedMedicine)).GenericName;
        var item = new PrescriptionItem { Id = Guid.NewGuid(), Medicine = name, Strength = "Synthetic strength",
            Dose = "Synthetic dose", DoseUnit = "Synthetic dose unit", Route = "Synthetic route",
            Frequency = "Synthetic schedule", Duration = "Synthetic duration", Quantity = 5, DispenseUnit = PharmacyUnit.Tablet };
        var rx = new ClinicalRecord { Id = Guid.NewGuid(), EncounterId = encounter.Id, Kind = ClinicalRecordKind.Prescription,
            Status = ClinicalStatus.Draft, RecordedDate = new DateOnly(2026, 11, 24), Items = [item],
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = clinician.Id, UpdatedByUserId = clinician.Id, ConcurrencyToken = Guid.NewGuid() };
        db.AddRange(department, patient, doctor, appointment, encounter, rx);
        await db.SaveChangesAsync();
        rx.Status = ClinicalStatus.Finalized;
        rx.FinalizedAtUtc = now;
        rx.FinalizedByUserId = clinician.Id;
        await db.SaveChangesAsync();
        var actor = Principal(pharm.Id, HospitalRoles.Pharmacist);
        var code = suffix[..20].ToUpperInvariant();
        if (!sharedMedicine.HasValue)
            await pharmacy.SaveMedicineAsync(actor, null, Catalog(code, name), default);
        var medicine = await db.Medicines.SingleAsync(m => sharedMedicine.HasValue ? m.Id == sharedMedicine : m.Code == code);
        return new(actor, Principal(clinician.Id, HospitalRoles.Doctor), rx.Id, item.Id, medicine.Id, code,
            patient.PatientNumber, DateOnly.FromDateTime(now.AddHours(5.5)));
    }

    private static ClaimsPrincipal Principal(Guid id, string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)], "Tests"));

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private async Task SignInAsync(HttpClient client, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var email = $"pharmacy-{Guid.NewGuid():N}@example.test";
        var account = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsEnabled = true };
        const string syntheticPassword = "Synthetic!Pharm345";
        Assert.True((await users.CreateAsync(account, syntheticPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(account, role)).Succeeded);
        var html = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = email, ["Password"] = syntheticPassword, ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 24, 18, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed record Fixture(ClaimsPrincipal Pharmacist, ClaimsPrincipal Doctor, Guid Rx, Guid Item,
        Guid MedicineId, string MedicineCode, string PatientNumber, DateOnly Today);
}
