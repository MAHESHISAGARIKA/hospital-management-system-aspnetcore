using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class StaffTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Theory]
    [InlineData("/Staff")]
    [InlineData("/Staff/My")]
    [InlineData("/Staff/Reports")]
    [InlineData("/Staff/Leave/Approvals")]
    public async Task StaffPagesRequireAuthentication(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ClockInRequiresAuthentication()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsync("/Staff/Attendance/ClockIn", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Login", response.Headers.Location!.OriginalString);
    }
}
