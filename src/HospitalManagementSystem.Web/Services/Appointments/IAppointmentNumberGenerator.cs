namespace HospitalManagementSystem.Web.Services.Appointments;

public interface IAppointmentNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
