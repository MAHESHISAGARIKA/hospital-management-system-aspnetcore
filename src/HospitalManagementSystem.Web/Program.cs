using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services;
using HospitalManagementSystem.Web.Services.Appointments;
using HospitalManagementSystem.Web.Services.Clinical;
using HospitalManagementSystem.Web.Services.Doctors;
using HospitalManagementSystem.Web.Services.Patients;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var hospitalDatabaseConnection = builder.Configuration.GetConnectionString("HospitalDatabase");

if (string.IsNullOrWhiteSpace(hospitalDatabaseConnection))
{
    throw new InvalidOperationException(
        "The 'HospitalDatabase' connection string is missing. Configure ConnectionStrings:HospitalDatabase using user secrets for local development.");
}

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, such as production deployment.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await RoleInitializer.InitializeAsync(roleManager);
    }

    if (app.Environment.IsDevelopment() &&
        args.Any(argument => string.Equals(argument, "--bootstrap-first-admin", StringComparison.Ordinal)))
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var adminUsers = await userManager.GetUsersInRoleAsync(HospitalRoles.Administrator);
        var activeAdmin = adminUsers.FirstOrDefault(user => user.IsEnabled && !user.LockoutEnd.HasValue);
        if (activeAdmin is not null)
        {
            throw new InvalidOperationException("First Administrator bootstrap refused: an active Administrator already exists.");
        }

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
        {
            await userManager.DeleteAsync(admin);
            throw new InvalidOperationException("Administrator bootstrap could not assign the Administrator role.");
        }

        await auditLogService.WriteAsync(
            "FirstAdministratorCreated",
            admin.Id,
            admin.Id,
            "Created through the deliberate local bootstrap procedure.");

        Console.WriteLine("First Administrator account created successfully.");
    }
}

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
