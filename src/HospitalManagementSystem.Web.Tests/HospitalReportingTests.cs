using System.Net;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services.Appointments;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class HospitalReportingTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    private const string Period = "?from=2026-02-01&to=2026-02-01";

    [Theory]
    [InlineData(HospitalRoles.Administrator, "Patients", "Billing")]
    [InlineData(HospitalRoles.Accountant, "Billing", "Patients")]
    [InlineData(HospitalRoles.Pharmacist, "Pharmacy", "Laboratory")]
    [InlineData(HospitalRoles.LaboratoryStaff, "Laboratory", "Staff")]
    [InlineData(HospitalRoles.Doctor, "Appointments", "Patients")]
    [InlineData(HospitalRoles.Nurse, "Inpatients", "Appointments")]
    [InlineData(HospitalRoles.Receptionist, "Patients", "Billing")]
    public async Task ReportReadPrintAndExportFollowRolePermissions(string role, string allowed, string denied)
    {
        var (client, actor) = await SignIn(role);
        using var session = client;
        foreach (var suffix in new[] { "", "/Print", "/Csv" })
        {
            using var success = await client.GetAsync($"/Reports/{allowed}{suffix}{Period}");
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.True(success.Headers.CacheControl?.NoStore);
            using var forbidden = await client.GetAsync($"/Reports/{denied}{suffix}{Period}");
            Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
            Assert.Contains("/Account/AccessDenied", forbidden.Headers.Location!.OriginalString);
        }

        using var dashboard = await client.GetAsync($"/Dashboard{Period}");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.True(dashboard.Headers.CacheControl?.NoStore);
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Hospital Dashboard", html);
        if (role != HospitalRoles.Accountant) Assert.DoesNotContain("Payments collected", html);

        using var invalid = await client.GetAsync($"/Reports/{allowed}?from=2026-03-01&to=2026-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var audit = await db.AuditLogEntries.Where(a => a.ActorUserId == actor && a.Action.StartsWith("Report")).ToArrayAsync();
        Assert.Contains(audit, a => a.Action == "ReportExported" && a.Details == allowed);
        Assert.All(audit.Where(a => a.Action == "ReportAccessDenied"), a => Assert.Equal(denied, a.Details));
    }

    [Fact]
    public async Task PatientCountsUseLocalBoundariesAndDoNotDuplicateCompletedVisits()
    {
        var (client, _) = await SignIn(HospitalRoles.Administrator);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var start = AppointmentService.ToUtc(new(2026, 2, 1), TimeOnly.MinValue);
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = "Fictional-report-1", FirstName = "Fictional",
            LastName = "Registration", Gender = "Unknown", CreatedAtUtc = start, IsActive = true };
        var archived = new Patient { Id = Guid.NewGuid(), PatientNumber = "Fictional-report-2", FirstName = "Fictional",
            LastName = "Registration", Gender = "Unknown", CreatedAtUtc = start.AddDays(1), IsActive = false };
        db.Patients.AddRange(patient, archived);
        for (var i = 0; i < 2; i++)
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), AppointmentNumber = Guid.NewGuid().ToString("N"),
                PatientId = patient.Id, StartAtUtc = start.AddHours(i), Status = AppointmentStatus.Completed });
        await db.SaveChangesAsync();
        var csv = await client.GetStringAsync($"/Reports/Patients/Csv{Period}&search=Fictional-report");
        Assert.Contains("\"Registered patients\",2,", csv);
        Assert.Contains("\"Archived patients\",1,", csv);
        Assert.Contains("\"New registrations\",1,", csv);
        Assert.Contains("\"Unique patients with completed appointments\",1,", csv);
        Assert.DoesNotContain("Fictional-report-1", csv);
        var html = await client.GetStringAsync($"/Reports/Patients{Period}&search=Fictional-report");
        Assert.Contains("Fictional-report-1", html);
        Assert.DoesNotContain("Fictional-report-2", html);
    }

    [Fact]
    public async Task DoctorFilterCannotExpandScopeAndCsvOmitsCancellationNarrative()
    {
        var (client, doctorAccount) = await SignIn(HospitalRoles.Doctor);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var department = new Department { Id = Guid.NewGuid(), Name = "=Fictional report", DepartmentCode = "ST13" };
        var ownDoctor = new Doctor { Id = Guid.NewGuid(), ApplicationUserId = doctorAccount, DepartmentId = department.Id,
            IsActive = true, FirstName = "Fictional", LastName = "Own" };
        var unrelated = new Doctor { Id = Guid.NewGuid(), DepartmentId = department.Id, IsActive = true, LastName = "Unrelated" };
        var start = AppointmentService.ToUtc(new(2026, 2, 1), TimeOnly.MinValue);
        var patient = new Patient { Id = Guid.NewGuid(), PatientNumber = "Fictional scope", Gender = "Unknown", IsActive = true };
        db.AddRange(department, ownDoctor, unrelated, patient);
        foreach (var doctor in new[] { ownDoctor, unrelated })
            db.Appointments.Add(new Appointment { Id = Guid.NewGuid(), AppointmentNumber = doctor.Id.ToString("N"),
                DoctorId = doctor.Id, PatientId = patient.Id, DepartmentId = department.Id, StartAtUtc = start, Status = AppointmentStatus.Cancelled,
                CancellationReason = "Private fictional cancellation" });
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync($"/Reports/Appointments/Csv{Period}");
        Assert.Contains("\"Cancelled\",1,", csv);
        Assert.Contains("\"'=Fictional report\",1,", csv);
        Assert.DoesNotContain("Private fictional cancellation", csv);
        Assert.DoesNotContain("Unrelated", csv);
        var filtered = await client.GetStringAsync($"/Reports/Appointments/Csv{Period}&doctor={unrelated.Id}");
        Assert.Contains("\"Cancelled\",0,", filtered);
    }

    [Fact]
    public async Task BillingSeparatesCashFromInvoicesAndIgnoresReversedPayments()
    {
        var (client, _) = await SignIn(HospitalRoles.Accountant);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var start = AppointmentService.ToUtc(new(2026, 4, 1), TimeOnly.MinValue);
        var issued = new PatientInvoice { Id = Guid.NewGuid(), Status = InvoiceStatus.Issued, IssuedAtUtc = start, Total = 100.25m };
        var voided = new PatientInvoice { Id = Guid.NewGuid(), Status = InvoiceStatus.Voided, IssuedAtUtc = start, Total = 10m };
        var partial = new PatientPayment { Id = Guid.NewGuid(), InvoiceId = issued.Id, Amount = 25.10m,
            Method = BillingPaymentMethod.Cash, RecordedAtUtc = start };
        var reversed = new PatientPayment { Id = Guid.NewGuid(), InvoiceId = issued.Id, Amount = 10.10m, RecordedAtUtc = start };
        db.AddRange(issued, voided, partial, reversed, new PaymentReversal { Id = Guid.NewGuid(), PaymentId = reversed.Id, CreatedAtUtc = start });
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync("/Reports/Billing/Csv?from=2026-04-01&to=2026-04-01");
        Assert.Contains("\"Issued invoice totals\",100.25,\"LKR\"", csv);
        Assert.Contains("\"Payments collected\",25.10,\"LKR\"", csv);
        Assert.Contains("\"Outstanding on invoices issued in period\",75.15,\"LKR\"", csv);
        Assert.Contains("\"Payments reversed in period\",10.10,\"LKR\"", csv);
        Assert.Contains("\"Partially paid invoices\",1,", csv);
    }

    [Fact]
    public async Task PharmacyCountsEligibleStockAndSeparatesUnits()
    {
        var (client, _) = await SignIn(HospitalRoles.Pharmacist);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var today = DateOnly.FromDateTime(AppointmentService.ToColombo(DateTime.UtcNow));
        var tablets = new Medicine { Id = Guid.NewGuid(), Code = "ST13T", GenericName = "Fictional tablets", IsActive = true,
            Unit = PharmacyUnit.Tablet, ReorderThreshold = 5 };
        var liquid = new Medicine { Id = Guid.NewGuid(), Code = "ST13L", GenericName = "Fictional liquid", IsActive = true,
            Unit = PharmacyUnit.Millilitre, ReorderThreshold = 1 };
        MedicineBatch Batch(Medicine medicine, decimal quantity, DateOnly expiry, BatchStatus status) =>
            new() { Id = Guid.NewGuid(), Number = Guid.NewGuid().ToString("N"), MedicineId = medicine.Id,
                Unit = medicine.Unit, Available = quantity, Expiry = expiry, Status = status };
        db.AddRange(tablets, liquid, Batch(tablets, 4, today, BatchStatus.Available),
            Batch(tablets, 9, today.AddDays(-1), BatchStatus.Available), Batch(tablets, 8, today, BatchStatus.Quarantined),
            Batch(liquid, 2.5m, today, BatchStatus.Available));
        var prescription = new ClinicalRecord { Id = Guid.NewGuid(), Kind = ClinicalRecordKind.Prescription };
        db.ClinicalRecords.Add(prescription);
        db.PrescriptionDispenses.Add(new PrescriptionDispense { Id = Guid.NewGuid(), PrescriptionId = prescription.Id, IsPartial = true,
            OccurredAtUtc = AppointmentService.ToUtc(new(2026, 2, 1), new(10, 0)) });
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync($"/Reports/Pharmacy/Csv{Period}");
        Assert.Contains("\"Physical stock by unit\",\"Tablet\",21,", csv);
        Assert.Contains("\"Eligible stock by unit\",\"Tablet\",4,", csv);
        Assert.Contains("\"Eligible stock by unit\",\"Millilitre\",2.5,", csv);
        Assert.Contains("\"Low-stock medicines\",1,", csv);
        Assert.Contains("\"Dispensing events\",\"Partial\",1,", csv);
    }

    [Fact]
    public async Task LaboratoryCountsOrdersOnceAndOmitsResultNarratives()
    {
        var (client, _) = await SignIn(HospitalRoles.LaboratoryStaff);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var utc = AppointmentService.ToUtc(new(2026, 5, 1), new(8, 0));
        var order = new LaboratoryOrder { Id = Guid.NewGuid(), Status = LabOrderStatus.Completed, CreatedAtUtc = utc };
        var first = new LaboratoryOrderItem { Id = Guid.NewGuid(), OrderId = order.Id, TestId = Guid.NewGuid(), Code = "T1" };
        var second = new LaboratoryOrderItem { Id = Guid.NewGuid(), OrderId = order.Id, TestId = Guid.NewGuid(), Code = "T2" };
        var accepted = new LaboratorySample { Id = Guid.NewGuid(), ItemId = first.Id, CreatedAtUtc = utc };
        var rejected = new LaboratorySample { Id = Guid.NewGuid(), ItemId = first.Id, CreatedAtUtc = utc, Status = LabSampleStatus.Rejected };
        var result = new LaboratoryResult { Id = Guid.NewGuid(), ItemId = first.Id, SampleId = accepted.Id,
            Status = LabResultStatus.Verified, VerifiedAtUtc = utc.AddMinutes(90), Note = "Fictional confidential laboratory note" };
        db.AddRange(order, first, second, accepted, rejected, result);
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync("/Reports/Laboratory/Csv?from=2026-05-01&to=2026-05-01");
        Assert.Contains("\"Total orders\",1,", csv);
        Assert.Contains("\"Completed orders\",1,", csv);
        Assert.Contains("\"Rejected samples\",1,", csv);
        Assert.Contains("\"Verified result versions\",1,", csv);
        Assert.Contains("\"Mean sample creation to verification (hours)\",1.5,", csv);
        Assert.DoesNotContain(result.Note, csv);
    }

    [Fact]
    public async Task StaffHoursCrossMidnightAndPrivateLeaveReasonStaysPrivate()
    {
        var (client, _) = await SignIn(HospitalRoles.Administrator);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var date = new DateOnly(2026, 6, 1);
        var midnight = AppointmentService.ToUtc(date, TimeOnly.MinValue);
        var employee = new Employee { Id = Guid.NewGuid(), Number = "EMP-Fictional-Stage13", Active = true };
        var leaveType = new StaffLeaveType { Id = Guid.NewGuid(), Code = "ST13", Name = "Fictional leave" };
        var leave = new StaffLeaveRequest { Id = Guid.NewGuid(), EmployeeId = employee.Id, TypeId = leaveType.Id, From = date,
            Through = date, Status = StaffLeaveStatus.Approved, Reason = "Fictional private leave reason" };
        db.AddRange(employee, leaveType, leave, new StaffAttendance { Id = Guid.NewGuid(), EmployeeId = employee.Id,
            InUtc = midnight.AddHours(-2), OutUtc = midnight.AddHours(3) });
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync($"/Reports/Staff/Csv?from=2026-06-01&to=2026-06-01&item={employee.Id}");
        Assert.Contains("\"Active employees\",1,", csv);
        Assert.Contains("\"Worked hours from completed sessions\",3,", csv);
        Assert.Contains("\"Approved\",1,", csv);
        Assert.DoesNotContain(leave.Reason, csv);
        Assert.DoesNotContain("Absence", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InpatientTransfersDoNotInflateCensusOrOccupancy()
    {
        var (client, _) = await SignIn(HospitalRoles.Receptionist);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var time = AppointmentService.ToUtc(new(2026, 7, 1), new(8, 0));
        var ward = new HospitalWard { Id = Guid.NewGuid(), Name = "Fictional ward", Active = true };
        var priorBed = new HospitalBed { Id = Guid.NewGuid(), WardId = ward.Id, Condition = BedCondition.Maintenance, Active = true };
        var currentBed = new HospitalBed { Id = Guid.NewGuid(), WardId = ward.Id, Condition = BedCondition.Ready, Active = true };
        var readyBed = new HospitalBed { Id = Guid.NewGuid(), WardId = ward.Id, Condition = BedCondition.Ready, Active = true };
        var admission = new InpatientAdmission { Id = Guid.NewGuid(), Status = AdmissionStatus.Admitted, AdmittedAtUtc = time };
        var discharged = new InpatientAdmission { Id = Guid.NewGuid(), Status = AdmissionStatus.Discharged, AdmittedAtUtc = time.AddDays(-1) };
        db.AddRange(ward, priorBed, currentBed, readyBed, admission, discharged,
            new BedAllocation { Id = Guid.NewGuid(), AdmissionId = admission.Id, BedId = priorBed.Id, StartedAtUtc = time, EndedAtUtc = time.AddHours(1) },
            new BedAllocation { Id = Guid.NewGuid(), AdmissionId = admission.Id, BedId = currentBed.Id, StartedAtUtc = time.AddHours(1) },
            new InpatientDischarge { Id = Guid.NewGuid(), AdmissionId = discharged.Id, SignedAtUtc = time });
        await db.SaveChangesAsync();

        var csv = await client.GetStringAsync("/Reports/Inpatients/Csv?from=2026-07-01&to=2026-07-01");
        Assert.Contains("\"Current inpatients\",1,", csv);
        Assert.Contains("\"Admissions in period\",1,", csv);
        Assert.Contains("\"Discharges in period\",1,", csv);
        Assert.Contains("\"Occupied beds\",1,", csv);
        Assert.Contains("\"Available beds\",1,", csv);
    }

    [Fact]
    public async Task PatientPaginationPreservesDateSearchStatusAndSort()
    {
        var (client, _) = await SignIn(HospitalRoles.Receptionist);
        using var session = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var start = AppointmentService.ToUtc(new(2026, 8, 1), TimeOnly.MinValue);
        for (var index = 0; index < 26; index++)
        {
            db.Patients.Add(new Patient { Id = Guid.NewGuid(), PatientNumber = $"Fictional-page-{index:D2}",
                FirstName = "Fictional", LastName = "Page", Gender = "Unknown", IsActive = true, CreatedAtUtc = start.AddMinutes(index) });
        }
        await db.SaveChangesAsync();

        const string filters = "from=2026-08-01&to=2026-08-01&search=Fictional-page&status=Active&order=oldest";
        var html = await client.GetStringAsync("/Reports/Patients?" + filters);
        Assert.Contains("Fictional-page-00", html);
        Assert.DoesNotContain("Fictional-page-25", html);
        var link = Regex.Matches(html, "href=\"([^\"]+)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Single(url => url.StartsWith("/Reports/Patients?", StringComparison.Ordinal) && url.Contains("page=2"));
        foreach (var item in filters.Split('&'))
            Assert.Contains(item, link);
        var second = await client.GetStringAsync(link);
        Assert.Contains("Fictional-page-25", second);
        Assert.DoesNotContain("Fictional-page-00", second);
    }

    [PostgreSqlFact]
    public async Task NpgsqlExecutesAllReportAggregatesAgainstPreparedDisposableSchema()
    {
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HOSPITAL_APPOINTMENT_TEST_CONNECTION"));
        if (string.Equals(connection.Database, "hospital_management_dev", StringComparison.OrdinalIgnoreCase) ||
            connection.Database?.Contains("test", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("Reporting integration tests require an explicitly disposable test database.");

        await using var db = new HospitalDbContext(new DbContextOptionsBuilder<HospitalDbContext>().UseNpgsql(connection.ConnectionString).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var transaction = await db.Database.BeginTransactionAsync();
        var actor = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"fictional-{Guid.NewGuid():N}", IsEnabled = true };
        db.Users.Add(actor);
        await db.SaveChangesAsync();
        var claims = new List<System.Security.Claims.Claim> { new(System.Security.Claims.ClaimTypes.NameIdentifier, actor.Id.ToString()) };
        claims.AddRange(new[] { HospitalRoles.Administrator, HospitalRoles.Accountant, HospitalRoles.Pharmacist }
            .Select(role => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)));
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "Test"));
        var service = new HospitalManagementSystem.Web.Services.Reporting.HospitalReportingService(db, TimeProvider.System);
        var filter = new HospitalManagementSystem.Web.Models.ViewModels.ReportFilter();
        Assert.True(service.Normalize(filter));

        foreach (var kind in Enum.GetValues<HospitalManagementSystem.Web.Models.ViewModels.ReportKind>())
            Assert.NotEmpty((await service.Read(principal, kind, filter, false, default)).Groups);

        await transaction.RollbackAsync();
    }

    private async Task<(HttpClient Client, Guid Actor)> SignIn(string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, IsEnabled = true };
        const string password = "Fictional!Report673";
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var token = Regex.Match(await client.GetStringAsync("/Account/Login"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) });
        using var response = await client.PostAsync("/Account/Login", form);
        Assert.Equal("/Dashboard", response.Headers.Location?.OriginalString);
        return (client, user.Id);
    }
}

