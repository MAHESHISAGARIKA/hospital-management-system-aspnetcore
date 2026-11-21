using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;

namespace HospitalManagementSystem.Web.Services.Doctors;

public interface IDoctorScheduleService
{
    Task<IReadOnlyCollection<DoctorSchedule>> GetForDoctorAsync(Guid doctorId, CancellationToken cancellationToken = default);
    Task<DoctorSchedule> AddAsync(DoctorScheduleViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<DoctorSchedule> UpdateAsync(DoctorScheduleViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid scheduleId, Guid actorUserId, CancellationToken cancellationToken = default);
}
