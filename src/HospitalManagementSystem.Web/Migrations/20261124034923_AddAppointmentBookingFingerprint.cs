using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentBookingFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_idempotency_key",
                table: "appointments");

            migrationBuilder.AddColumn<string>(
                name: "BookingRequestFingerprint",
                table: "appointments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointments_actor_idempotency_key",
                table: "appointments",
                columns: new[] { "CreatedByUserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_actor_idempotency_key",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "BookingRequestFingerprint",
                table: "appointments");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_idempotency_key",
                table: "appointments",
                column: "IdempotencyKey",
                unique: true);
        }
    }
}
