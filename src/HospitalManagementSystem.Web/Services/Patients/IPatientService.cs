using HospitalManagementSystem.Web.Models;
using HospitalManagementSystem.Web.Models.ViewModels;

namespace HospitalManagementSystem.Web.Services.Patients;

public interface IPatientService
{
    Task<Patient> RegisterAsync(PatientEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<Patient?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PatientSearchViewModel> SearchAsync(PatientSearchViewModel criteria, CancellationToken cancellationToken = default);
    Task<Patient> UpdateAsync(PatientEditViewModel model, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task ReactivateAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Patient>> FindPossibleDuplicatesAsync(PatientEditViewModel model, CancellationToken cancellationToken = default);
}
