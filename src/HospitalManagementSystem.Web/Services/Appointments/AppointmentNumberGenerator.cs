using HospitalManagementSystem.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Appointments;

public sealed class AppointmentNumberGenerator(HospitalDbContext dbContext) : IAppointmentNumberGenerator
{
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var sequence = await dbContext.Database
            .SqlQuery<long>($"SELECT nextval('appointment_number_sequence') AS \"Value\"")
            .FirstAsync(cancellationToken);
        return $"APT-{DateTime.UtcNow.Year}-{sequence:D8}";
    }
}
