using HospitalManagementSystem.Web.Data;
using HospitalManagementSystem.Web.Models;

namespace HospitalManagementSystem.Web.Services;

public sealed class AuditLogService(HospitalDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    : IAuditLogService
{
    public async Task WriteAsync(
        string action,
        Guid? actorUserId,
        Guid? targetUserId,
        string? details,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var safeDetails = details;
        if (details is not null &&
            (details.Contains("password", StringComparison.OrdinalIgnoreCase) ||
             details.Contains("token", StringComparison.OrdinalIgnoreCase) ||
             details.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
             details.Contains("passwordhash", StringComparison.OrdinalIgnoreCase)))
        {
            safeDetails = "Sensitive authentication details omitted.";
        }

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            Action = action,
            TargetUserId = targetUserId?.ToString("D"),
            Details = safeDetails is null ? null : safeDetails.Length <= 500 ? safeDetails : safeDetails[..500],
            IpAddress = ipAddress.Length <= 45 ? ipAddress : ipAddress[..45],
            OccurredAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
