namespace HospitalManagementSystem.Web.Services.Patients;

public interface IPatientNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
