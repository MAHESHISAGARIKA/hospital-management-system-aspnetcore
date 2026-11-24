using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;

namespace HospitalManagementSystem.Web.Services.Appointments;

public interface IAppointmentService
{
    Task<Appointment> BookAsync(AppointmentCreateViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<Appointment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AppointmentSearchViewModel> SearchAsync(AppointmentSearchViewModel criteria, Guid? doctorAccountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TimeOnly>> GetAvailableSlotsAsync(Guid doctorId, DateOnly localDate, int durationMinutes, CancellationToken cancellationToken = default);
    Task<Appointment> RescheduleAsync(Guid id, AppointmentRescheduleViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid id, AppointmentCancellationViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<Appointment> ChangeStatusAsync(Guid id, AppointmentStatusViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AppointmentCalendarViewModel> GetCalendarAsync(DateOnly date, string view, Guid? doctorId, Guid? doctorAccountId, CancellationToken cancellationToken = default);
}
