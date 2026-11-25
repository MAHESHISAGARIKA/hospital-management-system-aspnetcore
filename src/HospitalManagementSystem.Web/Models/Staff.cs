namespace HospitalManagementSystem.Web.Models;

public enum StaffLeaveStatus { Pending, Approved, Rejected, Withdrawn }

public sealed class Employee
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkEmail { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public DateOnly Joined { get; set; }
    public bool Active { get; set; }
    public Guid? UserId { get; set; }
    public Guid Token { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid UpdatedBy { get; set; }
}
public sealed class StaffDepartment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public DateTime FromUtc { get; set; }
    public DateTime? UntilUtc { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class StaffAttendance
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime InUtc { get; set; }
    public DateTime? OutUtc { get; set; }
    public Guid ActorId { get; set; }
}
public sealed class StaffAttendanceCorrection
{
    public Guid Id { get; set; }
    public Guid AttendanceId { get; set; }
    public DateTime PriorInUtc { get; set; }
    public DateTime? PriorOutUtc { get; set; }
    public DateTime InUtc { get; set; }
    public DateTime OutUtc { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid ActorId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class StaffLeaveType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public Guid Token { get; set; }
}
public sealed class StaffLeaveRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TypeId { get; set; }
    public StaffLeaveType Type { get; set; } = null!;
    public DateOnly From { get; set; }
    public DateOnly Through { get; set; }
    public string Reason { get; set; } = "";
    public StaffLeaveStatus Status { get; set; }
    public Guid ActorId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid Token { get; set; }
}
public sealed class StaffLeaveDecision
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorId { get; set; }
    public DateTime AtUtc { get; set; }
    public StaffLeaveStatus Status { get; set; }
    public string? Reason { get; set; }
}
