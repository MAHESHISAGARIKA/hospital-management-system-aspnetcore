using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Billing;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Inpatients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class InpatientTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Fact]
    public async Task WardBedAdmissionTransferAndDischargePreserveHistory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<InpatientService>();
        var f = await SeedAsync(db);
        var ward = await WardAsync(service, f);
        var first = await BedAsync(service, f, ward);
        var second = await BedAsync(service, f, ward);
        var maintenance = await BedAsync(service, f, ward, BedCondition.Maintenance);
        var id = await PendingAsync(service, f);
        var pending = await service.DetailsAsync(f.Doctor, id, false, default);
        Assert.Equal(AdmissionStatus.Pending, pending.Admission.Status);
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(f.Reception, id, Act(pending, first), false, default));
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.ApproveAsync(f.Reception, id, Act(pending), default));
        await service.ApproveAsync(f.Doctor, id, Act(pending), default);
        var approved = await service.DetailsAsync(f.Reception, id, false, default);
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(f.Reception, id, Act(approved, maintenance), false, default));
        var wrongWard = Act(approved, first);
        wrongWard.WardId = Guid.NewGuid();
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(f.Reception, id, wrongWard, false, default));
        await service.AllocateAsync(f.Reception, id, Act(approved, first), false, default);
        var active = await service.DetailsAsync(f.Doctor, id, false, default);
        Assert.Equal(AdmissionStatus.Admitted, active.Admission.Status);
        var occupied = await service.BedAsync(f.Admin, first, default);
        await Assert.ThrowsAsync<StayException>(() => service.SaveBedAsync(f.Admin, first,
            new() { WardId = ward, Label = occupied.Label, Token = occupied.Token }, default));
        var loadedWard = await service.WardAsync(f.Admin, ward, default);
        await Assert.ThrowsAsync<StayException>(() => service.SaveWardAsync(f.Admin, ward,
            new() { Code = loadedWard.Code, Name = loadedWard.Name, Active = false, Token = loadedWard.Token }, default));
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(f.Reception, id, Act(active, first), true, default));
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(f.Reception, id, Act(active, maintenance), true, default));
        Assert.Single((await service.DetailsAsync(f.Doctor, id, false, default)).Admission.Allocations);
        await service.AllocateAsync(f.Reception, id, Act(active, second), true, default);
        var transferred = await service.DetailsAsync(f.Doctor, id, false, default);
        Assert.Equal(2, transferred.Admission.Allocations.Count);
        var old = Assert.Single(transferred.Admission.Allocations, a => a.BedId == first);
        var current = Assert.Single(transferred.Admission.Allocations, a => a.EndedAtUtc == null);
        Assert.Equal(old.EndedAtUtc, current.StartedAtUtc);
        Assert.Equal(BedCondition.Cleaning, (await service.BedAsync(f.Admin, first, default)).Condition);
        var discharge = new DischargeInput { Token = transferred.Admission.Token, Disposition = DischargeDisposition.Home, Diagnosis = "Fictional signed diagnosis", Summary = "Fictional signed summary" };
        discharge.Disposition = null;
        await Assert.ThrowsAsync<StayException>(() => service.DischargeAsync(f.Doctor, id, discharge, default));
        discharge.Disposition = DischargeDisposition.Home;
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.DischargeAsync(f.Reception, id, discharge, default));
        discharge.Token = active.Admission.Token;
        await Assert.ThrowsAsync<StayException>(() => service.DischargeAsync(f.Doctor, id, discharge, default));
        discharge.Token = transferred.Admission.Token;
        await service.DischargeAsync(f.Doctor, id, discharge, default);
        await Assert.ThrowsAsync<StayException>(() => service.DischargeAsync(f.Doctor, id, discharge, default));
        var report = await service.DetailsAsync(f.Doctor, id, true, default);
        Assert.Equal(AdmissionStatus.Discharged, report.Admission.Status);
        Assert.Equal(discharge.Summary, report.Admission.Discharge!.Summary);
        Assert.All(report.Admission.Allocations, a => Assert.NotNull(a.EndedAtUtc));
        Assert.Equal(BedCondition.Cleaning, (await service.BedAsync(f.Admin, second, default)).Condition);
        Assert.Null((await service.DetailsAsync(f.Reception, id, false, default)).Admission.Discharge);
        Assert.Empty(await db.PatientInvoices.Where(invoice => invoice.PatientId == f.Patient).ToArrayAsync());
        Assert.DoesNotContain(await db.AuditLogEntries.ToArrayAsync(), a => a.Details?.Contains("Fictional signed") == true);
    }

    [Fact]
    public async Task RegistrationIsIdempotentRejectsIneligiblePeopleAndPreventsDoubleOccupancy()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<InpatientService>();
        var f = await SeedAsync(db);
        var other = await SeedAsync(db);
        var ward = await WardAsync(service, f);
        await Assert.ThrowsAsync<StayException>(() => service.SaveWardAsync(f.Admin, null,
            new() { Code = f.Key, Name = "Duplicate" }, default));
        await Assert.ThrowsAsync<StayException>(() => service.SaveWardAsync(f.Admin, null,
            new() { Code = f.Key + "B", Name = "Invalid department", DepartmentId = Guid.NewGuid() }, default));
        var bed = await BedAsync(service, f, ward);
        var registered = await service.BedAsync(f.Admin, bed, default);
        await Assert.ThrowsAsync<StayException>(() => service.SaveBedAsync(f.Admin, null,
            new() { WardId = ward, Label = registered.Label }, default));
        var request = Register(f);
        request.PatientId = Guid.NewGuid();
        await Assert.ThrowsAsync<StayException>(() => service.RegisterAsync(f.Reception, request, default));
        request.PatientId = f.Patient;
        var patient = await db.Patients.FindAsync(f.Patient);
        patient!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<StayException>(() => service.RegisterAsync(f.Reception, request, default));
        patient.IsActive = true;
        var doctor = await db.Doctors.FindAsync(f.DoctorId);
        doctor!.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<StayException>(() => service.RegisterAsync(f.Reception, request, default));
        doctor.IsActive = true;
        await db.SaveChangesAsync();
        var a = await service.RegisterAsync(f.Reception, request, default);
        Assert.Equal(a, await service.RegisterAsync(f.Reception, request, default));
        await Assert.ThrowsAsync<StayException>(() => service.RegisterAsync(f.Reception, Register(f), default));
        var b = await PendingAsync(service, other);
        Assert.NotEqual(a, b);
        await ApproveAsync(service, f, a);
        await ApproveAsync(service, other, b);
        await service.AllocateAsync(f.Reception, a, Act(await service.DetailsAsync(f.Reception, a, false, default), bed), false, default);
        var waiting = await service.DetailsAsync(other.Reception, b, false, default);
        await Assert.ThrowsAsync<StayException>(() => service.AllocateAsync(other.Reception, b, Act(waiting, bed), false, default));
        Assert.Empty((await service.BedsAsync(f.Admin, new() { WardId = ward, Available = true }, default)).Rows);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.DetailsAsync(other.Doctor, a, false, default));
        await Assert.ThrowsAsync<StayException>(() => service.CancelAsync(other.Reception, b, new() { Token = waiting.Admission.Token }, default));
        await service.CancelAsync(other.Reception, b, new() { Token = waiting.Admission.Token, Reason = "Fictional cancellation" }, default);
        Assert.Equal(AdmissionStatus.Cancelled, (await service.DetailsAsync(other.Doctor, b, false, default)).Admission.Status);
    }

    [Fact]
    public async Task AssignedNurseCanObserveAndCorrectDuringActiveStayOnly()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<HospitalDbContext>();
        var service = sp.GetRequiredService<InpatientService>();
        var f = await SeedAsync(db);
        var nurseManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(HospitalRoles.Nurse))
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(HospitalRoles.Nurse))).Succeeded);
        var nurseUser = await nurseManager.FindByIdAsync(f.NurseId.ToString());
        Assert.True((await nurseManager.AddToRoleAsync(nurseUser!, HospitalRoles.Nurse)).Succeeded);
        var ward = await WardAsync(service, f);
        var bed = await BedAsync(service, f, ward);
        var id = await PendingAsync(service, f);
        await ApproveAsync(service, f, id);
        await service.AllocateAsync(f.Reception, id, Act(await service.DetailsAsync(f.Reception, id, false, default), bed), false, default);
        var active = await service.DetailsAsync(f.Doctor, id, false, default);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.DetailsAsync(f.Nurse, id, false, default));
        var assignment = Act(active);
        assignment.NurseUserId = f.NurseId;
        await service.AssignAsync(f.Doctor, id, assignment, false, default);
        var assigned = await service.DetailsAsync(f.Nurse, id, false, default);
        Assert.True(assigned.Nurse);
        var input = new ObservationInput { Token = assigned.Admission.Token, Kind = ObservationKind.TemperatureCelsius, Value = 12.34m };
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.ObserveAsync(f.Reception, id, input, default));
        await service.ObserveAsync(f.Nurse, id, input, default);
        assigned = await service.DetailsAsync(f.Nurse, id, false, default);
        var original = Assert.Single(assigned.Admission.Observations);
        input.Token = assigned.Admission.Token;
        input.CorrectsId = original.Id;
        input.Reason = "Fictional observation correction";
        input.Value = 12.35m;
        await service.ObserveAsync(f.Nurse, id, input, default);
        var after = await service.DetailsAsync(f.Doctor, id, false, default);
        Assert.Equal(2, after.Admission.Observations.Count);
        Assert.Equal(12.34m, after.Admission.Observations.Single(o => o.Id == original.Id).Value);
        var end = Act(after);
        end.AssignmentId = Assert.Single(after.Admission.Assignments).Id;
        await service.AssignAsync(f.Doctor, id, end, true, default);
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.DetailsAsync(f.Nurse, id, false, default));
        Assert.DoesNotContain(await db.AuditLogEntries.ToArrayAsync(), a => a.Details?.Contains("Fictional observation correction") == true);
    }

    [Fact]
    public async Task OutpatientConversionKeepsOriginalVisitAndAdmissionIsBillableOnceDischarged()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<HospitalDbContext>();
        var service = sp.GetRequiredService<InpatientService>();
        var clinical = sp.GetRequiredService<ClinicalRecordService>();
        var billing = sp.GetRequiredService<BillingService>();
        var appointments = sp.GetRequiredService<IAppointmentService>();
        var f = await SeedAsync(db);
        var list = await service.OutpatientsAsync(f.Doctor, new() { DoctorId = f.DoctorId }, true, appointments, default);
        Assert.Contains(list.Appointments, x => x.Id == f.Visit);
        Assert.DoesNotContain((await service.OutpatientsAsync(f.Doctor, new() { Date = service.Today.AddDays(-1) }, false, appointments, default))
            .Appointments, x => x.Id == f.Visit);
        var checkedIn = await db.Appointments.FindAsync(f.Visit);
        var actor = Guid.Parse(f.Reception.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await appointments.ChangeStatusAsync(f.Visit, new AppointmentStatusViewModel
        { Status = AppointmentStatus.CheckedIn, ConcurrencyToken = checkedIn!.ConcurrencyToken }, actor, default);
        var encounter = await clinical.OpenAsync(f.Doctor, f.Visit, default);
        var summary = await service.VisitAsync(f.Doctor, f.Visit, appointments, default);
        Assert.Equal(encounter, summary.EncounterId);
        var input = Register(f);
        input.EncounterId = encounter;
        await Assert.ThrowsAsync<ClinicalAccessException>(() => service.RegisterAsync(f.Reception, input, default));
        var id = await service.RegisterAsync(f.Doctor, input, default);
        Assert.Equal(id, await service.RegisterAsync(f.Doctor, input, default));
        Assert.Equal(AppointmentStatus.CheckedIn, (await db.Appointments.FindAsync(f.Visit))!.Status);
        await ApproveAsync(service, f, id);
        var ward = await WardAsync(service, f);
        var bed = await BedAsync(service, f, ward);
        await service.AllocateAsync(f.Reception, id, Act(await service.DetailsAsync(f.Reception, id, false, default), bed), false, default);
        var active = await service.DetailsAsync(f.Doctor, id, false, default);
        await service.DischargeAsync(f.Doctor, id, new()
        { Token = active.Admission.Token, Disposition = DischargeDisposition.Home, Diagnosis = "Fictional diagnosis", Summary = "Fictional treatment summary" }, default);
        var catalog = await billing.SaveServiceAsync(f.Accountant, null, new()
        { Code = f.Key, Name = "Fictional stay service", Category = BillingCategory.Admission }, default);
        await billing.AddPriceAsync(f.Accountant, catalog, new()
        { From = billing.Today, Until = billing.Today.AddDays(1), Amount = 10m }, default);
        var draft = new InvoiceInput { PatientId = f.Patient, ServiceDate = billing.Today,
            Lines = [new() { ServiceId = catalog, SourceId = id }] };
        var unrelated = await SeedAsync(db);
        draft.PatientId = unrelated.Patient;
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(f.Accountant, null, draft, default));
        draft.PatientId = f.Patient;
        var invoice = await billing.SaveDraftAsync(f.Accountant, null, draft, default);
        var state = await billing.InvoiceAsync(f.Accountant, invoice, false, default);
        await billing.IssueAsync(f.Accountant, invoice, state.Invoice.Token, default);
        Assert.Equal(id, Assert.Single((await billing.InvoiceAsync(f.Accountant, invoice, false, default)).Invoice.Items).AdmissionId);
        await Assert.ThrowsAsync<BillingException>(() => billing.SaveDraftAsync(f.Accountant, null, draft, default));
    }

    [Theory]
    [InlineData(HospitalRoles.Administrator)]
    [InlineData(HospitalRoles.Receptionist)]
    [InlineData(HospitalRoles.Nurse)]
    [InlineData(HospitalRoles.Accountant)]
    [InlineData(HospitalRoles.LaboratoryStaff)]
    [InlineData(HospitalRoles.Pharmacist)]
    public async Task NonDoctorsCannotOpenSignedDischarge(string role)
    {
        using var client = Client();
        await SignInAsync(client, role);
        using var response = await client.GetAsync($"/Inpatients/DischargeSummary/{Guid.NewGuid()}");
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ReceptionistWorklistsUseNoStoreAndPostRequiresAntiforgery()
    {
        using var client = Client();
        await SignInAsync(client, HospitalRoles.Receptionist);
        foreach (var path in new[] { "/Inpatients", "/Inpatients/Admissions/Create", "/Inpatients/Beds", "/Inpatients/Wards",
            "/Inpatients/CurrentPatients", "/Outpatients", "/Outpatients/Visits" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {response.StatusCode}.");
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var post = await client.PostAsync("/Inpatients/Admissions/Create", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlPreventsConcurrentInpatientBedAllocation()
    {
        var connectionString = await PostgreSqlTestDatabase.VerifiedConnectionStringAsync();
        var registrations = new ServiceCollection();
        registrations.AddLogging().AddDbContext<HospitalDbContext>(options => options.UseNpgsql(connectionString));
        registrations.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<HospitalDbContext>();
        registrations.AddSingleton(TimeProvider.System);
        registrations.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        registrations.AddScoped<IAuditLogService, AuditLogService>();
        registrations.AddScoped<ClinicalRecordService>();
        registrations.AddScoped<InpatientService>();
        using var app = registrations.BuildServiceProvider();
        using var scope = app.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var service = scope.ServiceProvider.GetRequiredService<InpatientService>();
        var f = await SeedAsync(db);
        var competitor = await SeedAsync(db);
        var ward = await WardAsync(service, f);
        var bed = await BedAsync(service, f, ward);
        var a = await PendingAsync(service, f);
        var b = await PendingAsync(service, competitor);
        await ApproveAsync(service, f, a);
        await ApproveAsync(service, competitor, b);
        var first = Act(await service.DetailsAsync(f.Reception, a, false, default), bed);
        var second = Act(await service.DetailsAsync(competitor.Reception, b, false, default), bed);
        async Task<bool> Try(Guid id, Fixture fixture, AdmissionAction action)
        {
            using var request = app.CreateScope();
            try { await request.ServiceProvider.GetRequiredService<InpatientService>().AllocateAsync(fixture.Reception, id, action, false, default); return true; }
            catch (StayException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Try(a, f, first), Try(b, competitor, second)), accepted => accepted);
        var allocation = await db.BedAllocations.AsNoTracking().SingleAsync(x => x.BedId == bed && x.EndedAtUtc == null);
        var winner = allocation.AdmissionId == a ? f : competitor;
        db.ChangeTracker.Clear();
        var active = await service.DetailsAsync(winner.Doctor, allocation.AdmissionId, false, default);
        await service.DischargeAsync(winner.Doctor, allocation.AdmissionId, new()
        { Token = active.Admission.Token, Disposition = DischargeDisposition.Home, Diagnosis = "Fictional", Summary = "Fictional" }, default);
        Assert.Equal(BedCondition.Cleaning, (await service.BedAsync(f.Admin, bed, default)).Condition);
        Assert.NotNull((await db.BedAllocations.AsNoTracking().SingleAsync(x => x.Id == allocation.Id)).EndedAtUtc);

        var closed = await db.BedAllocations.AsNoTracking().SingleAsync(x => x.Id == allocation.Id);
        var clean = await service.BedAsync(f.Admin, bed, default);
        await service.SaveBedAsync(f.Admin, bed, new() { WardId = ward, Label = clean.Label, Token = clean.Token }, default);
        var overlap = new BedAllocation { Id = Guid.NewGuid(), AdmissionId = allocation.AdmissionId, BedId = bed,
            StartedAtUtc = closed.StartedAtUtc, EndedAtUtc = closed.EndedAtUtc, StartedByUserId = closed.StartedByUserId,
            EndedByUserId = closed.EndedByUserId };
        db.BedAllocations.Add(overlap);
        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23P01", Assert.IsType<PostgresException>(conflict.InnerException).SqlState);
        db.ChangeTracker.Clear();

        var waitingId = allocation.AdmissionId == a ? b : a;
        var waiting = allocation.AdmissionId == a ? competitor : f;
        var reserved = await db.InpatientAdmissions.AsNoTracking().SingleAsync(x => x.Id == waitingId);
        var duplicate = new InpatientAdmission { Id = Guid.NewGuid(), Number = $"ADM-{Guid.NewGuid():N}", PatientId = reserved.PatientId,
            DoctorId = reserved.DoctorId, RequestKey = Guid.NewGuid(), CreatedByUserId = reserved.CreatedByUserId,
            UpdatedByUserId = reserved.CreatedByUserId, Reason = "Fictional duplicate", Token = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.InpatientAdmissions.Add(duplicate);
        var duplicateError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(duplicateError.InnerException).SqlState);
        db.ChangeTracker.Clear();

        var opposite = await PendingAsync(service, winner);
        await ApproveAsync(service, winner, opposite);
        var secondBed = await BedAsync(service, f, ward);
        await service.AllocateAsync(waiting.Reception, waitingId, Act(await service.DetailsAsync(waiting.Reception, waitingId, false, default), bed), false, default);
        await service.AllocateAsync(winner.Reception, opposite, Act(await service.DetailsAsync(winner.Reception, opposite, false, default), secondBed), false, default);
        var sharedDestination = await BedAsync(service, f, ward);
        var oldWaiting = await service.DetailsAsync(waiting.Reception, waitingId, false, default);
        var oldOpposite = await service.DetailsAsync(winner.Reception, opposite, false, default);
        async Task<bool> Transfer(Guid id, Fixture fixture, AdmissionAction action)
        {
            using var request = app.CreateScope();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await request.ServiceProvider.GetRequiredService<InpatientService>()
                .AllocateAsync(fixture.Reception, id, action, true, timeout.Token); return true; }
            catch (StayException) { return false; }
        }
        using (var request = app.CreateScope())
        {
            var provider = request.ServiceProvider;
            var failing = new InpatientService(provider.GetRequiredService<HospitalDbContext>(), provider.GetRequiredService<ClinicalRecordService>(),
                new FaultAfterAudit(provider.GetRequiredService<IAuditLogService>()), provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<TimeProvider>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.AllocateAsync(
                waiting.Reception, waitingId, Act(oldWaiting, sharedDestination), true, default));
        }
        Assert.Single(await db.BedAllocations.AsNoTracking().Where(x => x.AdmissionId == waitingId).ToArrayAsync());
        Assert.Empty(await db.BedAllocations.AsNoTracking().Where(x => x.BedId == sharedDestination).ToArrayAsync());
        Assert.All(await Task.WhenAll(Transfer(waitingId, waiting, Act(oldWaiting, secondBed)),
            Transfer(opposite, winner, Act(oldOpposite, bed))), accepted => Assert.False(accepted));
        Assert.Single(await Task.WhenAll(Transfer(waitingId, waiting, Act(oldWaiting, sharedDestination)),
            Transfer(opposite, winner, Act(oldOpposite, sharedDestination))), accepted => accepted);
        Assert.Single(await db.BedAllocations.AsNoTracking().Where(x => x.BedId == sharedDestination && x.EndedAtUtc == null).ToArrayAsync());

    }

    private static async Task<Fixture> SeedAsync(HospitalDbContext db)
    {
        var now = DateTime.UtcNow;
        var key = Guid.NewGuid().ToString("N").ToUpperInvariant();
        ApplicationUser Account()
        {
            var name = $"{Guid.NewGuid():N}@example.test";
            var account = new ApplicationUser { Id = Guid.NewGuid(), Email = name, UserName = name, IsEnabled = true,
                NormalizedEmail = name.ToUpperInvariant(), NormalizedUserName = name.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Users.Add(account);
            return account;
        }
        var admin = Account(); var reception = Account(); var provider = Account(); var nurse = Account(); var accountant = Account();
        var dep = new Department { Id = Guid.NewGuid(), DepartmentCode = key[..10], Name = key, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = key[..20], FirstName = "Fictional", LastName = "Patient",
            IsActive = true, Gender = "Unknown", DateOfBirth = new DateOnly(1990, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        var doctor = new Doctor { Id = Guid.NewGuid(), DepartmentId = dep.Id, DoctorNumber = key[..20], RegistrationNumber = key[..20],
            FirstName = "Fictional", LastName = "Doctor", Specialization = "Example", ApplicationUserId = provider.Id,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var visit = new Appointment { Id = Guid.NewGuid(), AppointmentNumber = key[..20], IdempotencyKey = key, PatientId = patient.Id,
            DoctorId = doctor.Id, DepartmentId = dep.Id, StartAtUtc = now, EndAtUtc = now.AddMinutes(10),
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = reception.Id, UpdatedByUserId = reception.Id, ConcurrencyToken = Guid.NewGuid() };
        db.AddRange(dep, patient, doctor, visit);
        await db.SaveChangesAsync();
        return new(Principal(admin.Id, HospitalRoles.Administrator), Principal(reception.Id, HospitalRoles.Receptionist),
            Principal(provider.Id, HospitalRoles.Doctor), Principal(nurse.Id, HospitalRoles.Nurse), Principal(accountant.Id, HospitalRoles.Accountant),
            patient.Id, doctor.Id, nurse.Id, visit.Id, key[..20]);
    }

    private static ClaimsPrincipal Principal(Guid id, string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)], "Tests"));
    private static Task<Guid> WardAsync(InpatientService service, Fixture f)
        => service.SaveWardAsync(f.Admin, null, new() { Code = f.Key, Name = "Synthetic ward" }, default);
    private static Task<Guid> BedAsync(InpatientService service, Fixture f, Guid ward, BedCondition condition = BedCondition.Ready)
        => service.SaveBedAsync(f.Admin, null, new() { WardId = ward, Label = Guid.NewGuid().ToString("N"), Condition = condition }, default);
    private static AdmissionInput Register(Fixture f) => new()
        { RequestKey = Guid.NewGuid(), PatientId = f.Patient, DoctorId = f.DoctorId, Reason = "Fictional admission reason" };
    private static Task<Guid> PendingAsync(InpatientService service, Fixture f) => service.RegisterAsync(f.Reception, Register(f), default);
    private static AdmissionAction Act(AdmissionPage page, Guid bed = default)
        => new() { Token = page.Admission.Token, BedId = bed, Reason = "Fictional transfer administrative reason" };
    private static async Task ApproveAsync(InpatientService service, Fixture f, Guid id)
        => await service.ApproveAsync(f.Doctor, id, Act(await service.DetailsAsync(f.Doctor, id, false, default)), default);

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private async Task SignInAsync(HttpClient client, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var name = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = name, Email = name, EmailConfirmed = true, IsEnabled = true };
        const string sample = "Synthetic!Stay678";
        Assert.True((await users.CreateAsync(user, sample)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var html = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = name, ["Password"] = sample, ["__RequestVerificationToken"] = token.Groups[1].Value }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class FaultAfterAudit(IAuditLogService inner) : IAuditLogService
    {
        public async Task WriteAsync(string action, Guid? actorUserId, Guid? targetUserId, string? details, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(action, actorUserId, targetUserId, details, cancellationToken);
            throw new InvalidOperationException("Simulated transactional failure with fictional records.");
        }
    }

    private sealed record Fixture(ClaimsPrincipal Admin, ClaimsPrincipal Reception, ClaimsPrincipal Doctor, ClaimsPrincipal Nurse,
        ClaimsPrincipal Accountant, Guid Patient, Guid DoctorId, Guid NurseId, Guid Visit, string Key);
}
