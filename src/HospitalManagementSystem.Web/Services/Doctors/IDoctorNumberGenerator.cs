namespace HospitalManagementSystem.Web.Services.Doctors;

public interface IDoctorNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
