using HospitalManagementSystem.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Services.Patients;

public sealed class PatientNumberGenerator(HospitalDbContext dbContext) : IPatientNumberGenerator
{
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var next = await dbContext.Database
            .SqlQuery<long>($"SELECT nextval('patient_number_sequence') AS \"Value\"")
            .FirstAsync(cancellationToken);

        return $"PAT-{year}-{next:D6}";
    }
}
