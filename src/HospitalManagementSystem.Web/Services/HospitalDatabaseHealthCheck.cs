using HospitalManagementSystem.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HospitalManagementSystem.Web.Services;

public sealed class HospitalDatabaseHealthCheck(HospitalDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!database.Database.IsRelational())
            return HealthCheckResult.Unhealthy();

        try
        {
            if (!await database.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy();

            var pending = await database.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any() ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy();
        }
        catch (Npgsql.NpgsqlException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}

