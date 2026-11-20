namespace HospitalManagementSystem.Web.Authorization;

public static class HospitalRoles
{
    public const string Administrator = "Administrator";
    public const string Doctor = "Doctor";
    public const string Nurse = "Nurse";
    public const string Receptionist = "Receptionist";
    public const string LaboratoryStaff = "LaboratoryStaff";
    public const string Pharmacist = "Pharmacist";
    public const string Accountant = "Accountant";

    public static IReadOnlyCollection<string> All { get; } =
    [
        Administrator,
        Doctor,
        Nurse,
        Receptionist,
        LaboratoryStaff,
        Pharmacist,
        Accountant
    ];
}
