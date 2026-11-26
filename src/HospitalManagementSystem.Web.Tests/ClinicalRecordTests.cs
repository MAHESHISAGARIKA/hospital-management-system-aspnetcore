using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services.Clinical;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class ClinicalRecordTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Fact]
    public async Task CheckedInVisitCreatesOneEncounterAndCompletedVisitRetainsAccess()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db);
        var id = await service.OpenAsync(Doctor(visit), visit.AppointmentId, default);
        var encounter = Assert.Single((await service.EncounterAsync(Doctor(visit), id, false, null, default)).Encounters);
        Assert.Equal(visit.PatientId, encounter.PatientId);
        Assert.Equal(visit.DoctorId, encounter.DoctorId);
        Assert.Equal(DateTimeKind.Utc, encounter.CreatedAtUtc.Kind);
        await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.OpenAsync(Doctor(visit), visit.AppointmentId, default));
        var appointment = await db.Appointments.FindAsync(visit.AppointmentId);
        appointment!.Status = AppointmentStatus.Completed;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit), visit.AppointmentId, default));
        Assert.Equal(id, Assert.Single((await service.EncounterAsync(Doctor(visit), id, false, null, default)).Encounters).Id);
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Missed)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task IneligibleVisitCannotOpenEncounter(AppointmentStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db, status: status);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit), visit.AppointmentId, default));
    }

    [Fact]
    public async Task MissingArchivedInactiveAndMismatchedRelationshipsAreDenied()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db);
        var other = await SeedVisitAsync(db);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit), other.AppointmentId, default));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit), Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit with { Actor = Guid.NewGuid() }), visit.AppointmentId, default));

        var id = await service.OpenAsync(Doctor(visit), visit.AppointmentId, default);
        var appointment = await db.Appointments.FindAsync(visit.AppointmentId);
        appointment!.PatientId = other.PatientId;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.EncounterAsync(Doctor(visit), id, true, null, default));
        appointment.PatientId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.OpenAsync(Doctor(visit), visit.AppointmentId, default));
        appointment.PatientId = visit.PatientId;
        var patient = await db.Patients.FindAsync(visit.PatientId);
        patient!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.EncounterAsync(Doctor(visit), id, true, null, default));
        patient.IsActive = true;
        var doctor = await db.Doctors.FindAsync(visit.DoctorId);
        doctor!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.EncounterAsync(Doctor(visit), id, true, null, default));
    }

    [Fact]
    public async Task DiagnosisAndTreatmentDraftsFinalizeAndAmendWithoutChangingHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db);
        var user = Doctor(visit);
        var id = await service.OpenAsync(user, visit.AppointmentId, default);

        foreach (var kind in new[] { ClinicalRecordKind.Diagnosis, ClinicalRecordKind.Treatment })
        {
            var input = (await service.EditorAsync(user, id, null, kind, false, default)).Input;
            await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.SaveRecordAsync(user, id, null, false, input, default));
            input.Content = "Synthetic original clinical text";
            await service.SaveRecordAsync(user, id, null, false, input, default);
            var original = await db.ClinicalRecords.SingleAsync(r => r.EncounterId == id && r.Kind == kind);
            await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.SaveRecordAsync(user, id, null, false, input, default));
            var edit = (await service.EditorAsync(user, id, original.Id, kind, false, default)).Input;
            edit.Content = "Synthetic draft edit";
            await service.SaveRecordAsync(user, id, original.Id, false, edit, default);
            var unrelated = Doctor(await SeedVisitAsync(db));
            await Assert.ThrowsAsync<ClinicalAccessException>(() => service.SaveRecordAsync(
                unrelated, id, original.Id, false, edit, default));
            await service.FinalizeRecordAsync(user, id, original.Id, await TokensAsync(db, id, original), default);
            Assert.Equal(ClinicalStatus.Finalized, original.Status);
            Assert.Equal(visit.Actor, original.FinalizedByUserId);
            await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.SaveRecordAsync(user, id, original.Id, false, edit, default));

            var amendment = (await service.EditorAsync(user, id, original.Id, kind, true, default)).Input;
            amendment.Content = "Synthetic corrected text";
            amendment.AmendmentReason = "Synthetic amendment reason";
            await service.SaveRecordAsync(user, id, original.Id, true, amendment, default);
            var revision = await db.ClinicalRecords.SingleAsync(r => r.PreviousRevisionId == original.Id);
            await service.FinalizeRecordAsync(user, id, revision.Id, await TokensAsync(db, id, revision), default);
            Assert.Equal("Synthetic draft edit", original.Content);
            Assert.Equal("Synthetic corrected text", revision.Content);
        }

        await service.FinalizeEncounterAsync(user, id, (await db.ClinicalEncounters.FindAsync(id))!.ConcurrencyToken, default);
        var timeline = await service.PatientAsync(user, visit.PatientId, ClinicalRecordService.LocalToday, null, id, default);
        Assert.Equal(4, Assert.Single(timeline.Encounters).Records.Count);
        Assert.All(await db.AuditLogEntries.Where(a => a.ActorUserId == visit.Actor).ToListAsync(),
            entry => Assert.DoesNotContain("Synthetic", entry.Details ?? ""));
    }

    [Fact]
    public async Task PrescriptionDraftKeepsItemIdentityAndVoidPreservesFinalizedItems()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db);
        var user = Doctor(visit);
        var id = await service.OpenAsync(user, visit.AppointmentId, default);
        var input = (await service.EditorAsync(user, id, null, ClinicalRecordKind.Prescription, false, default)).Input;
        input.Medicines.Add(new() { Medicine = "Fictional incomplete line" });
        await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.SaveRecordAsync(user, id, null, false, input, default));
        input.Medicines = [Medicine("Fictional item A"), Medicine("Fictional item B")];
        await service.SaveRecordAsync(user, id, null, false, input, default);
        var record = await db.ClinicalRecords.Include(r => r.Items).SingleAsync(r => r.EncounterId == id);
        var first = record.Items.OrderBy(i => i.Position).First().Id;
        var edit = (await service.EditorAsync(user, id, record.Id, ClinicalRecordKind.Prescription, false, default)).Input;
        edit.Medicines.RemoveAt(1);
        edit.Medicines.Add(Medicine("Fictional item C"));
        await service.SaveRecordAsync(user, id, record.Id, false, edit, default);
        Assert.Equal(2, record.Items.Count);
        Assert.Contains(record.Items, i => i.Id == first);
        Assert.DoesNotContain(record.Items, i => i.Medicine == "Fictional item B");

        var tokens = await TokensAsync(db, id, record);
        await service.FinalizeRecordAsync(user, id, record.Id, tokens, default);
        await Assert.ThrowsAsync<ClinicalWorkflowException>(() => service.FinalizeRecordAsync(user, id, record.Id, tokens, default));
        var current = await TokensAsync(db, id, record);
        current.AmendmentReason = "Fictional void reason";
        await service.VoidPrescriptionAsync(user, id, record.Id, current, default);
        var voided = await db.ClinicalRecords.SingleAsync(r => r.PreviousRevisionId == record.Id);
        Assert.Equal(ClinicalStatus.Voided, voided.Status);
        Assert.Equal(ClinicalStatus.Finalized, record.Status);
        Assert.Equal(2, record.Items.Count);
        var unrelated = Doctor(await SeedVisitAsync(db));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.EncounterAsync(unrelated, id, true, record.Id, default));
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Receptionist)]
    [InlineData(HospitalRoles.Nurse)]
    [InlineData(HospitalRoles.Pharmacist)]
    [InlineData(HospitalRoles.Accountant)]
    [InlineData(HospitalRoles.LaboratoryStaff)]
    public async Task NonClinicalRolesAreDeniedEvenOnDirectUrls(string role)
    {
        using var client = CreateClient();
        await SignInAsync(client, role);
        using var response = await client.GetAsync($"/MedicalRecords/Report/{Guid.NewGuid()}");
        AssertDenied(response);
        using var post = await client.PostAsync("/MedicalRecords/SaveEntry", new FormUrlEncodedContent([]));
        AssertDenied(post);
    }

    [Fact]
    public async Task DoctorMvcWorkflowRequiresAntiforgeryAndReportIsScopedEncodedAndUncached()
    {
        using var client = CreateClient();
        var actor = await SignInAsync(client, HospitalRoles.Doctor);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClinicalRecordService>();
        var visit = await SeedVisitAsync(db, actor);
        var other = await SeedVisitAsync(db);
        var unrelatedId = await service.OpenAsync(Doctor(other), other.AppointmentId, default);
        using var forbidden = await client.GetAsync($"/MedicalRecords/Report/{unrelatedId}");
        AssertDenied(forbidden);
        using var invalidPost = await client.PostAsync("/MedicalRecords/Open", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["appointmentId"] = visit.AppointmentId.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPost.StatusCode);

        var token = await TokenAsync(client, "/MedicalRecords");
        using var open = await client.PostAsync("/MedicalRecords/Open", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["appointmentId"] = visit.AppointmentId.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        var encounter = await db.ClinicalEncounters.SingleAsync(e => e.AppointmentId == visit.AppointmentId);
        Assert.Contains(encounter.Id.ToString(), open.Headers.Location!.OriginalString);
        var editorUrl = $"/MedicalRecords/Entry/{encounter.Id}?kind=Diagnosis";
        var editorToken = await TokenAsync(client, editorUrl);
        using var save = await client.PostAsync($"/MedicalRecords/SaveEntry/{encounter.Id}", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = editorToken, ["Input.EncounterToken"] = encounter.ConcurrencyToken.ToString(),
                ["Input.Kind"] = "Diagnosis", ["Input.RecordedDate"] = ClinicalRecordService.LocalToday.ToString("yyyy-MM-dd"),
                ["Input.Content"] = "<b>Synthetic clinical text</b>"
            }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        using var report = await client.GetAsync($"/MedicalRecords/Report/{encounter.Id}");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.True(report.Headers.CacheControl?.NoStore);
        var html = await report.Content.ReadAsStringAsync();
        Assert.Contains("&lt;b&gt;Synthetic clinical text&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Synthetic clinical text</b>", html);
        Assert.DoesNotContain(other.PatientLabel, html);
        Assert.Contains(visit.PatientLabel, html);
        Assert.Contains("Print or save PDF", html);
        var prescriptionEditor = await client.GetStringAsync($"/MedicalRecords/Entry/{encounter.Id}?kind=Prescription");
        Assert.Contains("Input.Medicines[0].Medicine", prescriptionEditor);
        Assert.Contains("data-add-medicine", prescriptionEditor);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlRejectsRewritingFinalizedClinicalHistory()
    {
        var connectionString = await PostgreSqlTestDatabase.VerifiedConnectionStringAsync();

        var options = new DbContextOptionsBuilder<HospitalDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new HospitalDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var transaction = await db.Database.BeginTransactionAsync();
        var visit = await SeedVisitAsync(db);
        var user = Doctor(visit);
        var service = new ClinicalRecordService(db, new HospitalManagementSystem.Web.Services.AuditLogService(
            db, new Microsoft.AspNetCore.Http.HttpContextAccessor()));
        var id = await service.OpenAsync(user, visit.AppointmentId, default);
        var input = (await service.EditorAsync(user, id, null, ClinicalRecordKind.Diagnosis, false, default)).Input;
        input.Content = "Fictional signed record";
        await service.SaveRecordAsync(user, id, null, false, input, default);
        var record = await db.ClinicalRecords.SingleAsync(item => item.EncounterId == id);
        await service.FinalizeRecordAsync(user, id, record.Id, await TokensAsync(db, id, record), default);

        var invalidContent = "Fictional forbidden update";
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE clinical_records SET \"Content\" = {invalidContent} WHERE \"Id\" = {record.Id}"));
        Assert.Equal("23514", error.SqlState);
        await transaction.RollbackAsync();
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private async Task<Guid> SignInAsync(HttpClient client, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);

        const string testPassword = "Synthetic!Testing456";
        var email = $"emr-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsEnabled = true };
        Assert.True((await users.CreateAsync(user, testPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var token = await TokenAsync(client, "/Account/Login");
        using var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = testPassword, ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return user.Id;
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.OriginalString);
    }

    private static ClaimsPrincipal Doctor(Visit visit) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, visit.Actor.ToString()), new Claim(ClaimTypes.Role, HospitalRoles.Doctor)], "Test"));

    private static MedicineInput Medicine(string name) => new()
    {
        Medicine = name, Dose = "synthetic dose", DoseUnit = "synthetic unit", Route = "synthetic route",
        Frequency = "synthetic frequency", Duration = "synthetic duration"
    };

    private static async Task<ClinicalTransitionInput> TokensAsync(HospitalDbContext db, Guid id, ClinicalRecord record)
        => new() { EncounterToken = (await db.ClinicalEncounters.FindAsync(id))!.ConcurrencyToken, RecordToken = record.ConcurrencyToken };

    private static async Task<Visit> SeedVisitAsync(HospitalDbContext db, Guid? actor = null, AppointmentStatus status = AppointmentStatus.CheckedIn)
    {
        var key = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var user = actor ?? Guid.NewGuid();
        if (!actor.HasValue)
            db.Users.Add(new ApplicationUser
            {
                Id = user, UserName = key, Email = $"{key}@example.test",
                NormalizedUserName = key.ToUpperInvariant(), NormalizedEmail = $"{key}@example.test".ToUpperInvariant(),
                IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now
            });
        var department = new Department { Id = Guid.NewGuid(), Name = "Fictional department " + key, DepartmentCode = key[..10], IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var patient = new Patient
        {
            Id = Guid.NewGuid(), PatientNumber = key[..20], FirstName = "Fictional", LastName = "Patient",
            Gender = "Unknown", IsActive = true, DateOfBirth = new DateOnly(1990, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var doctor = new Doctor
        {
            Id = Guid.NewGuid(), ApplicationUserId = user, DepartmentId = department.Id, DoctorNumber = key[..20],
            FirstName = "Fictional", LastName = "Doctor", RegistrationNumber = key[..20], Specialization = "Synthetic", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), PatientId = patient.Id, DoctorId = doctor.Id, DepartmentId = department.Id,
            AppointmentNumber = key[..20], IdempotencyKey = key, Status = status, StartAtUtc = now,
            EndAtUtc = now.AddMinutes(15), CreatedByUserId = user, UpdatedByUserId = user, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.AddRange(department, patient, doctor, appointment);
        await db.SaveChangesAsync();
        return new(user, doctor.Id, patient.Id, appointment.Id, patient.PatientNumber);
    }

    private sealed record Visit(Guid Actor, Guid DoctorId, Guid PatientId, Guid AppointmentId, string PatientLabel);
}
