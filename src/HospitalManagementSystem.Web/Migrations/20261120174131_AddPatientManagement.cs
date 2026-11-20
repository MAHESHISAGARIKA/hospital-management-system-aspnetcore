using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BloodGroup",
                table: "patients",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "patients",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "patients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gender",
                table: "patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "patients",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "NIC",
                table: "patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "patient_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_documents_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patients_identity_match",
                table: "patients",
                columns: new[] { "LastName", "FirstName", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_nic",
                table: "patients",
                column: "NIC");

            migrationBuilder.CreateIndex(
                name: "IX_patient_documents_PatientId",
                table: "patient_documents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_documents_StoredFileName",
                table: "patient_documents",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS patient_number_sequence START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_documents");

            migrationBuilder.DropIndex(
                name: "ix_patients_identity_match",
                table: "patients");

            migrationBuilder.DropIndex(
                name: "ix_patients_nic",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "BloodGroup",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "NIC",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "patients");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS patient_number_sequence;");
        }
    }
}
