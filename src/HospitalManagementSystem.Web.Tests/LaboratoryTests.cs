using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Laboratory;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class LaboratoryTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Fact]
    public async Task CatalogCanBeEditedDeactivatedAndRejectsDuplicateCodes()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var lab = scope.ServiceProvider.GetRequiredService<LaboratoryService>();
        var seed = await SeedAsync(db, lab);
        var input = Catalog(seed.Test.Code);
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.SaveTestAsync(seed.Admin, null, input, default));
        input.Name = "Synthetic renamed test";
        input.Token = seed.Test.ConcurrencyToken;
        input.Active = false;
        await lab.SaveTestAsync(seed.Admin, seed.Test.Id, input, default);
        Assert.False((await lab.CatalogAsync(seed.Admin, seed.Test.Code, default)).Single().Active);
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.RequestAsync(seed.Doctor, seed.EncounterId,
            new() { RequestKey = Guid.NewGuid(), TestIds = [seed.Test.Id] }, default));
    }

    [Fact]
    public async Task RequestsAreScopedIdempotentAndCancelWithHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var lab = scope.ServiceProvider.GetRequiredService<LaboratoryService>();
        var seed = await SeedAsync(db, lab);
        var unrelated = await SeedAsync(db, lab);
        var input = new LabOrderInput { RequestKey = Guid.NewGuid(), TestIds = [seed.Test.Id, seed.OtherTest.Id],
            Instructions = "Synthetic private request" };
        await Assert.ThrowsAsync<ClinicalAccessException>(() => lab.RequestAsync(seed.Doctor, Guid.NewGuid(), input, default));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => lab.RequestAsync(unrelated.Doctor, seed.EncounterId, input, default));
        var id = await lab.RequestAsync(seed.Doctor, seed.EncounterId, input, default);
        Assert.Equal(id, await lab.RequestAsync(seed.Doctor, seed.EncounterId, input, default));
        input.Instructions = "Different synthetic request";
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.RequestAsync(seed.Doctor, seed.EncounterId, input, default));

        var order = (await lab.DetailsAsync(seed.Doctor, id, false, default)).Order;
        Assert.Equal(2, order.Items.Count);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => lab.DetailsAsync(unrelated.Doctor, id, false, default));
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.CancelAsync(seed.Doctor, id, new() { Token = order.ConcurrencyToken }, default));
        await lab.CancelAsync(seed.Doctor, id, new() { Token = order.ConcurrencyToken, Reason = "Synthetic cancellation" }, default);
        order = (await lab.DetailsAsync(seed.Doctor, id, false, default)).Order;
        Assert.Equal(LabOrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAtUtc);
        Assert.Equal(2, order.Items.Count);
        foreach (var entry in await db.AuditLogEntries.Where(a => a.Action.StartsWith("Laboratory")).ToArrayAsync())
            Assert.DoesNotContain("Synthetic", entry.Details ?? "");
    }

    [Fact]
    public async Task SpecimensRequireIdentityConfirmationAndSupportRecollection()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var lab = scope.ServiceProvider.GetRequiredService<LaboratoryService>();
        var seed = await SeedAsync(db, lab);
        var id = await CreateOrderAsync(lab, seed);
        var order = await StaffOrderAsync(lab, seed, id);
        var item = order.Items.Single();
        var input = Action(order, item);
        input.PatientNumber = "incorrect";
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.CollectAsync(seed.Staff, id, input, default));
        input = Action(order, item);
        await lab.CollectAsync(seed.Staff, id, input, default);
        order = await StaffOrderAsync(lab, seed, id);
        item = order.Items.Single();
        input = Action(order, item, item.Samples.Single());
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.ChangeSampleAsync(seed.Staff, id, input, LabSampleStatus.Processing, default));
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.ChangeSampleAsync(seed.Staff, id, input, LabSampleStatus.Rejected, default));
        input.Reason = "Synthetic specimen rejection";
        await lab.ChangeSampleAsync(seed.Staff, id, input, LabSampleStatus.Rejected, default);
        order = await StaffOrderAsync(lab, seed, id);
        item = order.Items.Single();
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.CancelAsync(seed.Doctor, id,
            new() { Token = order.ConcurrencyToken, Reason = "Synthetic cancellation" }, default));
        await lab.CollectAsync(seed.Staff, id, Action(order, item), default);
        order = await StaffOrderAsync(lab, seed, id);
        var samples = order.Items.Single().Samples;
        Assert.Equal(2, samples.Select(sample => sample.Accession).Distinct().Count());
        Assert.Contains(samples, sample => sample.Status == LabSampleStatus.Rejected && sample.RejectedAtUtc.HasValue);
        Assert.Contains(samples, sample => sample.Status == LabSampleStatus.Collected);
    }

    [Fact]
    public async Task MultiTestReleaseAndCorrectionsPreserveVerifiedResults()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var lab = scope.ServiceProvider.GetRequiredService<LaboratoryService>();
        var seed = await SeedAsync(db, lab);
        var id = await CreateOrderAsync(lab, seed, multiple: true);
        var initial = await StaffOrderAsync(lab, seed, id);
        await ReadyAsync(lab, seed, id, initial.Items.Single(i => i.TestId == seed.Test.Id).Id);
        await ReadyAsync(lab, seed, id, initial.Items.Single(i => i.TestId == seed.OtherTest.Id).Id);
        var order = await StaffOrderAsync(lab, seed, id);
        var quantitative = order.Items.Single(i => i.TestId == seed.Test.Id);
        var qualitative = order.Items.Single(i => i.TestId == seed.OtherTest.Id);
        var input = Result(order, quantitative);
        input.SampleId = qualitative.Samples.Single().Id;
        await Assert.ThrowsAsync<ClinicalAccessException>(() => lab.SaveResultAsync(seed.Staff, id, input, false, default));
        input = Result(order, quantitative);
        input.Value = 1.1234567m;
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.SaveResultAsync(seed.Staff, id, input, false, default));
        input.Value = 4.125m;
        await lab.SaveResultAsync(seed.Staff, id, input, false, default);
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.SaveResultAsync(seed.Staff, id, input, false, default));
        order = await StaffOrderAsync(lab, seed, id);
        quantitative = order.Items.Single(i => i.TestId == seed.Test.Id);
        input = Result(order, quantitative);
        input.ResultId = quantitative.Results.Single().Id;
        input.Value = 4.25m;
        await lab.SaveResultAsync(seed.Staff, id, input, false, default);
        var originalId = input.ResultId;
        order = await StaffOrderAsync(lab, seed, id);
        await lab.ChangeResultAsync(seed.Staff, id, Action(order, quantitative, result: originalId), LabResultStatus.Submitted, default);
        order = await StaffOrderAsync(lab, seed, id);
        await lab.ChangeResultAsync(seed.Staff, id, Action(order, quantitative, result: originalId), LabResultStatus.Draft, default);
        await SignAsync(lab, seed, id, quantitative.Id, originalId);
        Assert.All((await lab.DetailsAsync(seed.Doctor, id, false, default)).Order.Items, i => Assert.Empty(i.Results));
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.DetailsAsync(seed.Doctor, id, true, default));

        order = await StaffOrderAsync(lab, seed, id);
        qualitative = order.Items.Single(i => i.TestId == seed.OtherTest.Id);
        input = Result(order, qualitative);
        input.QualitativeValue = "Synthetic observation";
        await lab.SaveResultAsync(seed.Staff, id, input, false, default);
        await SignAsync(lab, seed, id, qualitative.Id,
            (await StaffOrderAsync(lab, seed, id)).Items.Single(i => i.Id == qualitative.Id).Results.Single().Id);
        var released = (await lab.DetailsAsync(seed.Doctor, id, true, default)).Order;
        Assert.Equal(LabOrderStatus.Completed, released.Status);
        Assert.All(released.Items, item => Assert.All(item.Results, result => Assert.Equal(LabResultStatus.Verified, result.Status)));

        order = await StaffOrderAsync(lab, seed, id);
        quantitative = order.Items.Single(i => i.Id == quantitative.Id);
        input = Result(order, quantitative);
        input.ResultId = originalId;
        input.Value = 5.125m;
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.SaveResultAsync(seed.Staff, id, input, false, default));
        await Assert.ThrowsAsync<LaboratoryException>(() => lab.SaveResultAsync(seed.Staff, id, input, true, default));
        input.AmendmentReason = "Synthetic correction";
        await lab.SaveResultAsync(seed.Staff, id, input, true, default);
        Assert.Equal(originalId, (await lab.DetailsAsync(seed.Doctor, id, true, default)).Order.Items
            .Single(i => i.Id == quantitative.Id).Results.Single().Id);
        var revision = (await StaffOrderAsync(lab, seed, id)).Items.Single(i => i.Id == quantitative.Id).Results.Single(r => r.PreviousId.HasValue);
        await SignAsync(lab, seed, id, quantitative.Id, revision.Id);
        var history = (await lab.DetailsAsync(seed.Doctor, id, true, default)).Order.Items.Single(i => i.Id == quantitative.Id).Results;
        Assert.Equal(2, history.Count);
        Assert.Equal(4.25m, history.Single(r => r.Id == originalId).Value);
        Assert.Equal(5.125m, history.Single(r => r.Id == revision.Id).Value);
        Assert.All(history, r => Assert.NotNull(r.VerifiedByUserId));
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Receptionist)]
    [InlineData(HospitalRoles.Nurse)]
    [InlineData(HospitalRoles.Pharmacist)]
    [InlineData(HospitalRoles.Accountant)]
    public async Task OtherRolesCannotOpenClinicalReports(string role)
    {
        using var client = Client();
        await SignInAsync(client, role);
        using var response = await client.GetAsync($"/Laboratory/Reports/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task StaffQueueAndRequestDetailsAreNoStoreAndPostRequiresAntiforgery()
    {
        using var client = Client();
        await SignInAsync(client, HospitalRoles.LaboratoryStaff);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var lab = scope.ServiceProvider.GetRequiredService<LaboratoryService>();
        var seed = await SeedAsync(db, lab);
        var id = await CreateOrderAsync(lab, seed);
        var order = await StaffOrderAsync(lab, seed, id);
        var queue = await client.GetStringAsync("/Laboratory");
        Assert.Contains(order.Number, queue);
        using var response = await client.GetAsync($"/Laboratory/Requests/Details/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Confirm patient number", html);
        using var blocked = await client.PostAsync($"/Laboratory/Samples/Collect/{id}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Token"] = order.ConcurrencyToken.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Empty((await StaffOrderAsync(lab, seed, id)).Items.Single().Samples);

        var itemId = order.Items.Single().Id;
        await ReadyAsync(lab, seed, id, itemId);
        Assert.Contains("Numeric result", await client.GetStringAsync($"/Laboratory/Requests/Details/{id}"));
        order = await StaffOrderAsync(lab, seed, id);
        var input = Result(order, order.Items.Single());
        input.Value = 2.5m;
        input.Note = "<em>Fictional laboratory note</em>";
        await lab.SaveResultAsync(seed.Staff, id, input, false, default);
        var result = (await StaffOrderAsync(lab, seed, id)).Items.Single().Results.Single();
        await SignAsync(lab, seed, id, itemId, result.Id);
        using var report = await client.GetAsync($"/Laboratory/Reports/{id}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.True(report.Headers.CacheControl?.NoStore);
        var printable = await report.Content.ReadAsStringAsync();
        Assert.Contains("Released laboratory report", printable);
        Assert.Contains("Print or save PDF", printable);
        Assert.Contains("&lt;em&gt;Fictional laboratory note&lt;/em&gt;", printable);
        Assert.DoesNotContain(input.Note, printable);
        Assert.DoesNotContain(await db.AuditLogEntries.ToArrayAsync(),
            entry => entry.Details?.Contains("Fictional laboratory note", StringComparison.Ordinal) == true);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlPreservesLaboratoryHistory()
    {
        var connectionString = await PostgreSqlTestDatabase.VerifiedConnectionStringAsync();
        var options = new DbContextOptionsBuilder<HospitalDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HospitalDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var transaction = await db.Database.BeginTransactionAsync();
        var audit = new AuditLogService(db, new HttpContextAccessor());
        var lab = new LaboratoryService(db, new ClinicalRecordService(db, audit), audit);
        var seed = await SeedAsync(db, lab);
        var id = await CreateOrderAsync(lab, seed, multiple: true);
        var secondOrder = await CreateOrderAsync(lab, seed);
        Assert.NotEqual((await StaffOrderAsync(lab, seed, id)).Number, (await StaffOrderAsync(lab, seed, secondOrder)).Number);
        var order = await StaffOrderAsync(lab, seed, id);
        var itemId = order.Items.Single(i => i.TestId == seed.Test.Id).Id;
        await ReadyAsync(lab, seed, id, itemId);
        await ReadyAsync(lab, seed, id, order.Items.Single(i => i.TestId == seed.OtherTest.Id).Id);
        order = await StaffOrderAsync(lab, seed, id);
        Assert.Equal(2, order.Items.SelectMany(i => i.Samples).Select(s => s.Accession).Distinct().Count());
        var input = Result(order, order.Items.Single(i => i.Id == itemId));
        input.Value = 2.5m;
        await lab.SaveResultAsync(seed.Staff, id, input, false, default);
        var result = (await StaffOrderAsync(lab, seed, id)).Items.Single(i => i.Id == itemId).Results.Single();
        await SignAsync(lab, seed, id, itemId, result.Id);

        var duplicateOrderId = Guid.NewGuid();
        var duplicateOrder = new LaboratoryOrder
        {
            Id = duplicateOrderId, Number = order.Number, EncounterId = seed.EncounterId, RequestKey = Guid.NewGuid(),
            CreatedByUserId = order.CreatedByUserId, UpdatedByUserId = order.CreatedByUserId,
            ConcurrencyToken = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.Add(duplicateOrder);
        var orderConflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(orderConflict.InnerException).SqlState);
        db.ChangeTracker.Clear();

        var pending = (await StaffOrderAsync(lab, seed, secondOrder)).Items.Single();
        var duplicateSample = new LaboratorySample
        {
            Id = Guid.NewGuid(), ItemId = pending.Id, Accession = order.Items.First().Samples.Single().Accession,
            CreatedByUserId = order.CreatedByUserId, UpdatedByUserId = order.CreatedByUserId,
            ConcurrencyToken = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.Add(duplicateSample);
        var accessionConflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(accessionConflict.InnerException).SqlState);
        db.ChangeTracker.Clear();

        var tracked = await db.LaboratoryOrders.SingleAsync(o => o.Id == id);
        var newToken = Guid.NewGuid();
        Assert.Equal(1, await db.LaboratoryOrders.Where(o => o.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.ConcurrencyToken, newToken)));
        tracked.UpdatedAtUtc = DateTime.UtcNow.AddSeconds(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS laboratory_completion_guard IMMEDIATE");

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE laboratory_results SET \"Value\" = {3.5m} WHERE \"Id\" = {result.Id}"));
        Assert.Equal("23514", error.SqlState);
        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.False(await db.LaboratoryOrders.AnyAsync(o => o.Id == id));
    }

    private static LabCatalogInput Catalog(string code) => new()
    { Code = code, Name = "Synthetic test", Specimen = "Synthetic specimen", Unit = "unit", Active = true };

    private static async Task<Fixture> SeedAsync(HospitalDbContext db, LaboratoryService lab)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        ApplicationUser Account(string role)
        {
            var email = $"{role}-{suffix}@example.test";
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email,
                NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
                IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Users.Add(user);
            return user;
        }
        var admin = Account("admin");
        var staff = Account("staff");
        var provider = Account("doctor");
        var department = new Department { Id = Guid.NewGuid(), DepartmentCode = suffix[..10], Name = suffix, IsActive = true,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = suffix[..20], FirstName = "Synthetic", LastName = "Patient",
            Gender = "Unknown", IsActive = true, DateOfBirth = new DateOnly(1990, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        var doctor = new Doctor { Id = Guid.NewGuid(), DoctorNumber = suffix[..20], RegistrationNumber = suffix[..20],
            FirstName = "Synthetic", LastName = "Doctor", Specialization = "Test", DepartmentId = department.Id,
            ApplicationUserId = provider.Id, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var appointment = new Appointment { Id = Guid.NewGuid(), AppointmentNumber = suffix[..20], IdempotencyKey = suffix,
            PatientId = patient.Id, DoctorId = doctor.Id, DepartmentId = department.Id, Status = AppointmentStatus.CheckedIn,
            StartAtUtc = now, EndAtUtc = now.AddMinutes(15), CreatedAtUtc = now, UpdatedAtUtc = now,
            CreatedByUserId = provider.Id, UpdatedByUserId = provider.Id };
        var encounter = new ClinicalEncounter { Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id,
            AppointmentId = appointment.Id, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = provider.Id,
            UpdatedByUserId = provider.Id, ConcurrencyToken = Guid.NewGuid() };
        db.AddRange(department, patient, doctor, appointment, encounter);
        await db.SaveChangesAsync();
        var administrator = Principal(admin.Id, HospitalRoles.Administrator);
        var code = suffix[..20].ToUpperInvariant();
        await lab.SaveTestAsync(administrator, null, Catalog(code + "A"), default);
        var qualitative = Catalog(code + "B");
        qualitative.Kind = LabResultKind.Qualitative;
        await lab.SaveTestAsync(administrator, null, qualitative, default);
        var tests = await db.LaboratoryTests.Where(t => t.Code.StartsWith(code)).OrderBy(t => t.Code).ToArrayAsync();
        return new(administrator, Principal(staff.Id, HospitalRoles.LaboratoryStaff),
            Principal(provider.Id, HospitalRoles.Doctor), encounter.Id, tests[0], tests[1]);
    }

    private static ClaimsPrincipal Principal(Guid id, string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)], "Tests"));

    private static Task<Guid> CreateOrderAsync(LaboratoryService service, Fixture seed, bool multiple = false)
        => service.RequestAsync(seed.Doctor, seed.EncounterId, new()
        { RequestKey = Guid.NewGuid(), TestIds = multiple ? [seed.Test.Id, seed.OtherTest.Id] : [seed.Test.Id] }, default);

    private static async Task<LaboratoryOrder> StaffOrderAsync(LaboratoryService service, Fixture seed, Guid id)
        => (await service.DetailsAsync(seed.Staff, id, false, default)).Order;

    private static LabActionInput Action(LaboratoryOrder order, LaboratoryOrderItem item, LaboratorySample? sample = null, Guid result = default)
        => new() { Token = order.ConcurrencyToken, ItemId = item.Id, SampleId = sample?.Id ?? Guid.Empty, ResultId = result,
            PatientNumber = order.Encounter.Patient.PatientNumber, OrderNumber = order.Number };

    private static LabResultInput Result(LaboratoryOrder order, LaboratoryOrderItem item)
        => new() { Token = order.ConcurrencyToken, ItemId = item.Id, SampleId = item.Samples.Single().Id };

    private static async Task ReadyAsync(LaboratoryService service, Fixture seed, Guid id, Guid itemId)
    {
        var order = await StaffOrderAsync(service, seed, id);
        await service.CollectAsync(seed.Staff, id, Action(order, order.Items.Single(i => i.Id == itemId)), default);
        foreach (var status in new[] { LabSampleStatus.Received, LabSampleStatus.Processing })
        {
            order = await StaffOrderAsync(service, seed, id);
            var item = order.Items.Single(i => i.Id == itemId);
            await service.ChangeSampleAsync(seed.Staff, id, Action(order, item, item.Samples.Single()), status, default);
        }
    }

    private static async Task SignAsync(LaboratoryService service, Fixture seed, Guid id, Guid itemId, Guid resultId)
    {
        var order = await StaffOrderAsync(service, seed, id);
        var input = Action(order, order.Items.Single(i => i.Id == itemId), result: resultId);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.ChangeResultAsync(seed.Doctor, id, input, LabResultStatus.Verified, default));
        await service.ChangeResultAsync(seed.Staff, id, input, LabResultStatus.Submitted, default);
        order = await StaffOrderAsync(service, seed, id);
        await service.ChangeResultAsync(seed.Staff, id, Action(order, order.Items.Single(i => i.Id == itemId), result: resultId), LabResultStatus.Verified, default);
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
        var email = $"lab-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsEnabled = true };
        const string syntheticPassword = "Synthetic!Lab345";
        Assert.True((await users.CreateAsync(user, syntheticPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var html = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = email, ["Password"] = syntheticPassword, ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed record Fixture(ClaimsPrincipal Admin, ClaimsPrincipal Staff, ClaimsPrincipal Doctor,
        Guid EncounterId, LaboratoryTest Test, LaboratoryTest OtherTest);
}
