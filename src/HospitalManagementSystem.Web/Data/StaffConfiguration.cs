using HospitalManagementSystem.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Web.Data;

internal static class StaffConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var employees = model.Entity<Employee>();
        employees.ToTable("staff_employees");
        employees.HasIndex(x => x.Number).IsUnique();
        employees.HasIndex(x => x.UserId).IsUnique();
        employees.HasIndex(x => x.Name);
        employees.Property(x => x.Token).IsConcurrencyToken();
        employees.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        var departments = model.Entity<StaffDepartment>();
        departments.ToTable("staff_departments", table => table.HasCheckConstraint("ck_staff_department_period", "\"UntilUtc\" IS NULL OR \"UntilUtc\" > \"FromUtc\""));
        departments.HasIndex(x => new { x.EmployeeId, x.FromUtc });
        departments.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        departments.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);

        var attendance = model.Entity<StaffAttendance>();
        attendance.ToTable("staff_attendance", table => table.HasCheckConstraint("ck_staff_attendance_period", "\"OutUtc\" IS NULL OR \"OutUtc\" > \"InUtc\""));
        attendance.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("\"OutUtc\" IS NULL");
        attendance.HasIndex(x => new { x.EmployeeId, x.InUtc });
        attendance.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        var corrections = model.Entity<StaffAttendanceCorrection>();
        corrections.ToTable("staff_attendance_corrections");
        corrections.HasOne<StaffAttendance>().WithMany().HasForeignKey(x => x.AttendanceId).OnDelete(DeleteBehavior.Restrict);

        var types = model.Entity<StaffLeaveType>();
        types.ToTable("staff_leave_types");
        types.HasIndex(x => x.Code).IsUnique();
        types.Property(x => x.Token).IsConcurrencyToken();

        var requests = model.Entity<StaffLeaveRequest>();
        requests.ToTable("staff_leave_requests", table =>
        {
            table.HasCheckConstraint("ck_staff_leave_period", "\"Through\" >= \"From\"");
            table.HasCheckConstraint("ck_staff_leave_status", "\"Status\" BETWEEN 0 AND 3");
        });
        requests.Property(x => x.Token).IsConcurrencyToken();
        requests.HasIndex(x => new { x.Status, x.From });
        requests.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        requests.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);

        var decisions = model.Entity<StaffLeaveDecision>();
        decisions.ToTable("staff_leave_decisions");
        decisions.HasIndex(x => x.RequestId).IsUnique();
        decisions.HasOne<StaffLeaveRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
