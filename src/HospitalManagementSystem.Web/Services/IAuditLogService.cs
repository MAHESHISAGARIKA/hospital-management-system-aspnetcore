namespace HospitalManagementSystem.Web.Services;

public interface IAuditLogService
{
    Task WriteAsync(
        string action,
        Guid? actorUserId,
        Guid? targetUserId,
        string? details,
        CancellationToken cancellationToken = default);
}
