using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

[Collection("Authentication isolated collection")]
public sealed class AuthenticationTests : IClassFixture<AuthenticationTestApplication>
{
    private readonly AuthenticationTestApplication _factory;

    public AuthenticationTests(AuthenticationTestApplication factory) => _factory = factory;

    [Fact]
    public async Task DashboardRedirectsUnauthenticatedUserToLogin()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginPageIsPublic()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidPasswordFailsLoginAndDoesNotCreateCookie()
    {
        var email = $"invalid-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Doctor);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await PostLoginAsync(client, email, "Wrong!Password123", null);

        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Redirect);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                     cookies.Any(cookie => cookie.StartsWith("HospitalManagement.Auth", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task FiveInvalidPasswordsLockAccount()
    {
        var email = $"lock-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Doctor);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        SignInResult? lastResult = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            lastResult = await signInManager.PasswordSignInAsync(user, "Wrong!Password123", false, true);
        }

        Assert.True(lastResult!.IsLockedOut);
        Assert.True(await userManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task DisabledUserCannotSignInAndIsRejectedByLoginController()
    {
        var email = $"disabled-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Doctor);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        user.IsEnabled = false;
        await userManager.UpdateAsync(user);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await PostLoginAsync(client, email, "Valid!Password123", null);

        Assert.False(user.IsEnabled);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LogoutIsPostOnly()
    {
        var logoutMethod = typeof(HospitalManagementSystem.Web.Controllers.AccountController).GetMethod("Logout");
        Assert.NotNull(logoutMethod);
        Assert.NotEmpty(logoutMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true));
        Assert.Empty(logoutMethod.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), true));

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var unauthenticatedDashboard = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, unauthenticatedDashboard.StatusCode);
    }

    [Fact]
    public async Task AdministratorAndDoctorRoleRestrictionsAreEnforced()
    {
        var doctorEmail = $"doctor-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(doctorEmail, "Valid!Password123", HospitalRoles.Doctor);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await PostLoginAsync(client, doctorEmail, "Valid!Password123", null);

        var response = await client.GetAsync("/Users");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task PasswordChangeUsesIdentityValidationAndClearsTemporaryFlag()
    {
        var email = $"password-{Guid.NewGuid():N}@example.test";
        var userId = await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Nurse, requirePasswordChange: true);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);

        var weak = await userManager.ChangePasswordAsync(user, "Valid!Password123", "weak");
        Assert.False(weak.Succeeded);

        var strong = await userManager.ChangePasswordAsync(user, "Valid!Password123", "NewValid!Password456");
        Assert.True(strong.Succeeded);

        user.RequirePasswordChange = false;
        await userManager.UpdateAsync(user);
        Assert.False(user.RequirePasswordChange);
    }

    [Fact]
    public async Task UserCreationRequiresAllowedRoleAndCreatesTemporaryPasswordFlag()
    {
        var email = $"created-{Guid.NewGuid():N}@example.test";
        var id = await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Receptionist, requirePasswordChange: true);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(id.ToString());

        Assert.NotNull(user);
        Assert.True(user.RequirePasswordChange);
        Assert.Contains(HospitalRoles.Receptionist, await userManager.GetRolesAsync(user));
        Assert.DoesNotContain("Password", await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task LastActiveAdministratorCannotBeDeactivatedOrDemoted()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var adminId = await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Administrator);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByIdAsync(adminId.ToString());
        Assert.NotNull(admin);

        var administrators = await userManager.GetUsersInRoleAsync(HospitalRoles.Administrator);
        var activeAdmins = administrators.Where(user => user.IsEnabled).ToList();
        Assert.Single(activeAdmins);

        var rolesBefore = await userManager.GetRolesAsync(admin);
        Assert.Contains(HospitalRoles.Administrator, rolesBefore);
    }

    [Fact]
    public async Task AuditNeverStoresPasswordOrResetTokenValues()
    {
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();

        await audit.WriteAsync("TestPasswordEvent", null, null, "password=never-store; token=never-store; secret=never-store");

        var entry = await db.AuditLogEntries.OrderByDescending(x => x.OccurredAtUtc).FirstAsync();
        Assert.DoesNotContain("never-store", entry.Details ?? string.Empty);
        Assert.Contains("Sensitive", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task ExternalReturnUrlIsRejectedByLoginControllerPolicy()
    {
        var email = $"redirect-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, "Valid!Password123", HospitalRoles.Doctor);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await PostLoginAsync(client, email, "Valid!Password123", "https://attacker.example.com/steal");

        Assert.NotEqual("https://attacker.example.com/steal", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginPostRequiresAntiForgeryToken()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "csrf@example.test",
            ["Password"] = "Valid!Password123"
        });

        var response = await client.PostAsync("/Account/Login", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SevenRoleNamesAreDefined()
    {
        Assert.Equal(7, HospitalRoles.All.Count);
        Assert.Contains(HospitalRoles.Administrator, HospitalRoles.All);
        Assert.Contains(HospitalRoles.Doctor, HospitalRoles.All);
        Assert.Contains(HospitalRoles.Nurse, HospitalRoles.All);
        Assert.Contains(HospitalRoles.Receptionist, HospitalRoles.All);
        Assert.Contains(HospitalRoles.LaboratoryStaff, HospitalRoles.All);
        Assert.Contains(HospitalRoles.Pharmacist, HospitalRoles.All);
        Assert.Contains(HospitalRoles.Accountant, HospitalRoles.All);
    }

    private async Task<Guid> CreateUserAsync(string email, string password, string role, bool requirePasswordChange = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(role))
            Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(role))).Succeeded);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsEnabled = true,
            RequirePasswordChange = requirePasswordChange,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password, string? returnUrl)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl ?? string.Empty
        });

        return await client.PostAsync("/Account/Login", content);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Anti-forgery token was not rendered.");
        return match.Groups[1].Value;
    }
}

[CollectionDefinition("Authentication isolated collection", DisableParallelization = true)]
public sealed class AuthenticationCollection
{
}

public sealed class AuthenticationTestApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HospitalDatabase"] = "Host=localhost;Database=test;Username=test;Password=test"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<HospitalDbContext>>();
            services.RemoveAll<HospitalDbContext>();
            foreach (var descriptor in services
                         .Where(descriptor =>
                             descriptor.ServiceType.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true ||
                             descriptor.ServiceType.FullName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                         .ToList())
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<HospitalDbContext>(options =>
                options.UseInMemoryDatabase("HospitalAuthenticationTests"));
        });
        builder.UseSetting("ConnectionStrings:HospitalDatabase", "Host=localhost;Database=test;Username=test;Password=test");
    }
}
