using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    WorkEmail = table.Column<string>(type: "text", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: false),
                    Joined = table.Column<DateOnly>(type: "date", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_employees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_leave_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_leave_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "staff_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    InUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OutUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_attendance", x => x.Id);
                    table.CheckConstraint("ck_staff_attendance_period", "\"OutUtc\" IS NULL OR \"OutUtc\" > \"InUtc\"");
                    table.ForeignKey(
                        name: "FK_staff_attendance_staff_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "staff_employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_departments", x => x.Id);
                    table.CheckConstraint("ck_staff_department_period", "\"UntilUtc\" IS NULL OR \"UntilUtc\" > \"FromUtc\"");
                    table.ForeignKey(
                        name: "FK_staff_departments_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_departments_staff_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "staff_employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_leave_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Through = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_leave_requests", x => x.Id);
                    table.CheckConstraint("ck_staff_leave_period", "\"Through\" >= \"From\"");
                    table.CheckConstraint("ck_staff_leave_status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_staff_leave_requests_staff_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "staff_employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_staff_leave_requests_staff_leave_types_TypeId",
                        column: x => x.TypeId,
                        principalTable: "staff_leave_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_attendance_corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriorInUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PriorOutUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OutUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_attendance_corrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_attendance_corrections_staff_attendance_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "staff_attendance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_leave_decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_leave_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_staff_leave_decisions_staff_leave_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "staff_leave_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE staff_departments ADD CONSTRAINT ex_staff_department_period
                    EXCLUDE USING gist ("EmployeeId" WITH =, tstzrange("FromUtc", "UntilUtc", '[)') WITH &&)
                    DEFERRABLE INITIALLY DEFERRED;
                ALTER TABLE staff_attendance ADD CONSTRAINT ex_staff_attendance_period
                    EXCLUDE USING gist ("EmployeeId" WITH =, tstzrange("InUtc", "OutUtc", '[)') WITH &&);
                ALTER TABLE staff_leave_requests ADD CONSTRAINT ex_staff_leave_overlap
                    EXCLUDE USING gist ("EmployeeId" WITH =, daterange("From", "Through", '[]') WITH &&)
                    WHERE ("Status" IN (0, 1));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_staff_attendance_EmployeeId",
                table: "staff_attendance",
                column: "EmployeeId",
                unique: true,
                filter: "\"OutUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_staff_attendance_EmployeeId_InUtc",
                table: "staff_attendance",
                columns: new[] { "EmployeeId", "InUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_attendance_corrections_AttendanceId",
                table: "staff_attendance_corrections",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_departments_DepartmentId",
                table: "staff_departments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_departments_EmployeeId_FromUtc",
                table: "staff_departments",
                columns: new[] { "EmployeeId", "FromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_employees_Name",
                table: "staff_employees",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_staff_employees_Number",
                table: "staff_employees",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_employees_UserId",
                table: "staff_employees",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_leave_decisions_RequestId",
                table: "staff_leave_decisions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_leave_requests_EmployeeId",
                table: "staff_leave_requests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_leave_requests_Status_From",
                table: "staff_leave_requests",
                columns: new[] { "Status", "From" });

            migrationBuilder.CreateIndex(
                name: "IX_staff_leave_requests_TypeId",
                table: "staff_leave_requests",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_leave_types_Code",
                table: "staff_leave_types",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_attendance_corrections");

            migrationBuilder.DropTable(
                name: "staff_departments");

            migrationBuilder.DropTable(
                name: "staff_leave_decisions");

            migrationBuilder.DropTable(
                name: "staff_attendance");

            migrationBuilder.DropTable(
                name: "staff_leave_requests");

            migrationBuilder.DropTable(
                name: "staff_employees");

            migrationBuilder.DropTable(
                name: "staff_leave_types");
        }
    }
}
