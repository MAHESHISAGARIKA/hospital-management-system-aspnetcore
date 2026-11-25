using System.Security.Claims;
using HospitalManagementSystem.Web.Authorization;
using HospitalManagementSystem.Web.Models.ViewModels;

namespace HospitalManagementSystem.Web.Services.Reporting;

public static class ReportAccess
{
    public static bool Registration(ClaimsPrincipal user) =>
        user.IsInRole(HospitalRoles.Administrator) || user.IsInRole(HospitalRoles.Receptionist);

    public static bool Allows(ClaimsPrincipal user, ReportKind kind) => kind switch
    {
        ReportKind.Patients => Registration(user),
        ReportKind.Appointments or ReportKind.Outpatients => Registration(user) || user.IsInRole(HospitalRoles.Doctor),
        ReportKind.Billing => user.IsInRole(HospitalRoles.Accountant),
        ReportKind.Pharmacy => user.IsInRole(HospitalRoles.Pharmacist) || user.IsInRole(HospitalRoles.Administrator),
        ReportKind.Laboratory => user.IsInRole(HospitalRoles.LaboratoryStaff) || user.IsInRole(HospitalRoles.Administrator),
        ReportKind.Staff => user.IsInRole(HospitalRoles.Administrator),
        ReportKind.Inpatients => Registration(user) || user.IsInRole(HospitalRoles.Nurse) || user.IsInRole(HospitalRoles.Doctor),
        _ => false
    };
}

