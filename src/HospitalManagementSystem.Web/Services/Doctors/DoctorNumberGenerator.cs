using HospitalManagementSystem.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Doctors;

public sealed class DoctorNumberGenerator(HospitalDbContext dbContext) : IDoctorNumberGenerator
{
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var next = await dbContext.Database
            .SqlQuery<long>($"SELECT nextval('doctor_number_sequence') AS \"Value\"")
            .FirstAsync(cancellationToken);

        return $"DOC-{year}-{next:D6}";
    }
}
