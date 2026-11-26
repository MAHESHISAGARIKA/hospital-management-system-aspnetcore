using HospitalManagementSystem.Web.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class DeploymentConfigurationTests
{
    [Fact]
    public void ExplicitConnectionTakesPrecedenceOutsideRailway()
    {
        const string connection = "Host=localhost;Database=hospital_management_dev;Username=local";
        Assert.Equal(connection, HospitalDatabaseConfiguration.Resolve(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:HospitalDatabase"] = connection, ["PGHOST"] = "unused" }).Build()));
    }

    [Fact]
    public void RailwayVariablesPreserveConnectionStringDelimitersInPassword()
    {
        var parsed = new NpgsqlConnectionStringBuilder(HospitalDatabaseConfiguration.Resolve(Railway()));
        Assert.Equal("demo.railway.internal", parsed.Host);
        Assert.Equal("fictional;value=\"example\"", parsed.Password);
        Assert.Equal(5432, parsed.Port);
    }

    [Theory]
    [InlineData("PGHOST", "public.example.test")]
    [InlineData("PGHOST", "public.example.test,demo.railway.internal")]
    [InlineData("PGDATABASE", "hospital_management_dev")]
    [InlineData("PGPORT", "0")]
    [InlineData("PGPASSWORD", "")]
    [InlineData("ConnectionStrings:HospitalDatabase", "Host=demo.railway.internal;Database=demo;Include Error Detail=true")]
    public void RejectsUnsafeOrIncompleteRailwayConfiguration(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => HospitalDatabaseConfiguration.Resolve(Railway(key, value)));

    [Theory]
    [InlineData("demo.up.railway.app", true)]
    [InlineData("demo.up.railway.app:443", true)]
    [InlineData("demo.up.railway.app:80", false)]
    [InlineData("demo.railway.internal", false)]
    public void EdgeModeUsesExactPublicHostAndHttpsPort(string host, bool expected)
    {
        Assert.Equal(expected, RailwayDemoHosting.MatchesEdgeHost(new Microsoft.AspNetCore.Http.HostString(host), "demo.up.railway.app"));
    }

    [Fact]
    public async Task ProductionBootstrapRefusesRedirectedInputBeforeReadingDatabaseConfiguration()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HospitalManagementSystem.Web.dll"));
        start.ArgumentList.Add("--bootstrap-first-admin");
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__HospitalDatabase"] = "Host=127.0.0.1;Port=1;Database=unreachable;Timeout=1";

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("First Administrator bootstrap requires an interactive terminal.", await stderr);
        Assert.DoesNotContain("Initial Administrator email", await stdout);
    }

    private static IConfiguration Railway(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?> {
            ["RAILWAY_SERVICE_ID"] = "synthetic", ["PGHOST"] = "demo.railway.internal", ["PGPORT"] = "5432",
            ["PGDATABASE"] = "hospital_demo", ["PGUSER"] = "demo_role", ["PGPASSWORD"] = "fictional;value=\"example\""
        };
        if (key != null) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}

