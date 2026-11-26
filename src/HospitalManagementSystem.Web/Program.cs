using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Doctors;
using HospitalManagementSystem.Web.Services.Patients;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

const string bootstrapFlag = "--bootstrap-first-admin";
var bootstrap = args.Contains(bootstrapFlag, StringComparer.Ordinal);
if (bootstrap && (Console.IsInputRedirected || Console.IsOutputRedirected))
{
    Console.Error.WriteLine("First Administrator bootstrap requires an interactive terminal.");
    Environment.ExitCode = 1;
    return;
}

var builder = WebApplication.CreateBuilder(args.Where(argument => argument != bootstrapFlag).ToArray());

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks().AddCheck<HospitalDatabaseHealthCheck>("hospital-database", tags: ["ready"]);

var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    if (!Path.IsPathFullyQualified(keyDirectory) || !Directory.Exists(keyDirectory))
        throw new InvalidOperationException("Configure DataProtection:KeyDirectory as an existing absolute directory.");
    builder.Services.AddDataProtection().SetApplicationName("HospitalManagementSystem")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
}

var railwayEdgeHost = RailwayDemoHosting.GetEdgeHost(builder.Configuration);
var proxyAddress = builder.Configuration["Hosting:TrustedProxy"];
if (railwayEdgeHost != null && !string.IsNullOrWhiteSpace(proxyAddress))
    throw new InvalidOperationException("Choose either the isolated Railway edge or a verified immediate proxy.");
if (!string.IsNullOrWhiteSpace(proxyAddress))
{
    if (!System.Net.IPAddress.TryParse(proxyAddress, out var trustedProxy))
        throw new InvalidOperationException("Hosting:TrustedProxy must be the immediate reverse proxy IP address.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(trustedProxy);
        options.ForwardLimit = 1;
    });
}

var hospitalDatabaseConnection = HospitalDatabaseConfiguration.Resolve(builder.Configuration);

builder.Services.AddDbContext<HospitalDbContext>(options =>
    options.UseNpgsql(hospitalDatabaseConnection));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.SignIn.RequireConfirmedEmail = false;
        options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
    })
    .AddEntityFrameworkStores<HospitalDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromMinutes(30);
});

builder.Services.AddOptions<AppointmentSchedulingOptions>()
    .BindConfiguration("AppointmentScheduling")
    .Validate(options => options.StartIntervalMinutes > 0 && 60 % options.StartIntervalMinutes == 0,
        "Appointment start interval must be a positive divisor of 60.")
    .Validate(options => options.AllowedDurationMinutes.Length > 0 &&
                         options.AllowedDurationMinutes.All(duration => duration > 0 && duration <= 240),
        "Appointment durations must contain supported positive values.")
    .ValidateOnStart();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "HospitalManagement.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ClinicalRecordService>();
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Inpatients.InpatientService>();
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Billing.BillingService>();
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Pharmacy.PharmacyService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Reporting.HospitalReportingService>();
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Staff.StaffService>();
builder.Services.AddOptions<HospitalManagementSystem.Web.Services.Pharmacy.PharmacyOptions>()
    .BindConfiguration("Pharmacy")
    .Validate(options => options.WarningDays is { Length: > 0 } && options.WarningDays.All(days => days is > 0 and <= 365),
        "Pharmacy warning periods must be from 1 to 365 days.")
    .ValidateOnStart();
builder.Services.AddScoped<HospitalManagementSystem.Web.Services.Laboratory.LaboratoryService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IPatientNumberGenerator, PatientNumberGenerator>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<IDoctorScheduleService, DoctorScheduleService>();
builder.Services.AddScoped<IDoctorNumberGenerator, DoctorNumberGenerator>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IAppointmentNumberGenerator, AppointmentNumberGenerator>();

var app = builder.Build();
if (!string.IsNullOrWhiteSpace(proxyAddress)) app.UseForwardedHeaders();

if (!string.IsNullOrEmpty(builder.Configuration["RAILWAY_SERVICE_ID"]))
{
    app.Use(async (context, next) =>
    {
        if (railwayEdgeHost != null && RailwayDemoHosting.MatchesEdgeHost(context.Request.Host, railwayEdgeHost))
            context.Request.Scheme = "https";

        if (!context.Request.IsHttps && context.Request.Path != "/health/live" && context.Request.Path != "/health/ready")
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        await next(context);
    });
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, such as production deployment.
    app.UseHsts();
}

app.UseWhen(context => context.Request.Path != "/health/live" && context.Request.Path != "/health/ready",
    branch => branch.UseHttpsRedirection());
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    if (bootstrap)
    {
        var database = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(78422106031)");
        await RoleInitializer.InitializeAsync(roleManager);

        if ((await userManager.GetUsersInRoleAsync(HospitalRoles.Administrator)).Count != 0)
            throw new InvalidOperationException("First Administrator bootstrap refused: an Administrator already exists.");

        Console.Write("Initial Administrator email: ");
        var email = Console.ReadLine()?.Trim();
        var password = ConsoleSecretReader.Read("Initial Administrator password: ");
        var passwordConfirmation = ConsoleSecretReader.Read("Confirm Initial Administrator password: ");

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new InvalidOperationException("A valid email address is required for Administrator bootstrap.");
        }

        if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Administrator bootstrap passwords do not match.");
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            throw new InvalidOperationException("Administrator bootstrap refused: that email already belongs to an existing account.");
        }

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsEnabled = true,
            RequirePasswordChange = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException("Administrator bootstrap failed due to password or account validation requirements.");
        }

        var roleResult = await userManager.AddToRoleAsync(admin, HospitalRoles.Administrator);
        if (!roleResult.Succeeded)
            throw new InvalidOperationException("Administrator bootstrap could not assign the Administrator role.");

        await auditLogService.WriteAsync(
            "FirstAdministratorCreated",
            admin.Id,
            admin.Id,
            "Created through the deliberate local bootstrap procedure.");

        await transaction.CommitAsync();
        Console.WriteLine("First Administrator account created successfully.");
        return;
    }
    if (!app.Environment.IsEnvironment("Testing"))
    {
        try
        {
            await RoleInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
        }
        catch (Npgsql.NpgsqlException) when (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("Hospital role initialization could not reach the expected database schema. Check connectivity, permissions and migrations.");
        }
    }
}

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
