using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class HealthEndpointTests(AuthenticationTestApplication factory) : IClassFixture<AuthenticationTestApplication>
{
    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK, "Healthy")]
    [InlineData("/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task HealthEndpointsSeparateLivenessFromDatabaseReadiness(string path, HttpStatusCode expected, string body)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
    }
}

