namespace HospitalManagementSystem.Web.Services.Appointments;

public sealed class AppointmentSchedulingOptions
{
    public int StartIntervalMinutes { get; set; } = 15;

    public int[] AllowedDurationMinutes { get; set; } = [15, 30, 45, 60];
}
