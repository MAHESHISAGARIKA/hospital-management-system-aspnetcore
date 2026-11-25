namespace HospitalManagementSystem.Web.Models.ViewModels;
public sealed class StaffInput
{
    public Guid Token { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Job { get; set; } = "";
    public string Code { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Active { get; set; }
    public Guid? Account { get; set; }
    public Guid Department { get; set; }
    public Guid Type { get; set; }
    public DateOnly From { get; set; }
    public DateOnly Through { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
public sealed record StaffPage(Employee[] Employees, StaffDepartment[] Departments, StaffAttendance[] Attendance,
    StaffLeaveRequest[] Leave, StaffLeaveType[] Types, bool Admin, Employee? Selected, int Page);

public sealed record StaffDepartmentCount(string Name, int Count);
