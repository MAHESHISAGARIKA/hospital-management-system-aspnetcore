using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;

namespace HospitalManagementSystem.Web.Services.Doctors;

public interface IDoctorService
{
    Task<Doctor> RegisterAsync(DoctorEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<Doctor?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DoctorSearchViewModel> SearchAsync(DoctorSearchViewModel criteria, CancellationToken cancellationToken = default);
    Task<Doctor> UpdateAsync(DoctorEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task AssignDepartmentAsync(Guid doctorId, Guid departmentId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task LinkAccountAsync(Guid doctorId, Guid applicationUserId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task UnlinkAccountAsync(Guid doctorId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Doctor>> FindPossibleDuplicatesAsync(DoctorEditViewModel model, CancellationToken cancellationToken = default);
}
