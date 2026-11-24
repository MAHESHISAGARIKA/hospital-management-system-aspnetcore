using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddElectronicMedicalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clinical_encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_encounters", x => x.Id);
                    table.CheckConstraint("ck_encounter_finalized", "(\"FinalizedAtUtc\" IS NULL) = (\"FinalizedByUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_clinical_encounters_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_encounters_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_encounters_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmendmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_records", x => x.Id);
                    table.UniqueConstraint("AK_clinical_records_Id_EncounterId_Kind", x => new { x.Id, x.EncounterId, x.Kind });
                    table.CheckConstraint("ck_clinical_record_finalized", "(\"Status\" = 'Draft' AND \"FinalizedAtUtc\" IS NULL AND \"FinalizedByUserId\" IS NULL) OR (\"Status\" <> 'Draft' AND \"FinalizedAtUtc\" IS NOT NULL AND \"FinalizedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("ck_clinical_record_kind", "\"Kind\" IN ('Diagnosis','Prescription','Treatment')");
                    table.CheckConstraint("ck_clinical_record_revision", "(\"PreviousRevisionId\" IS NULL AND \"AmendmentReason\" IS NULL AND \"Status\" <> 'Voided') OR (\"PreviousRevisionId\" IS NOT NULL AND length(trim(\"AmendmentReason\")) > 0 AND \"AmendmentReason\" IS NOT NULL)");
                    table.CheckConstraint("ck_clinical_record_status", "\"Status\" IN ('Draft','Finalized','Voided')");
                    table.ForeignKey(
                        name: "FK_clinical_records_clinical_encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "clinical_encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_records_clinical_records_PreviousRevisionId_Encoun~",
                        columns: x => new { x.PreviousRevisionId, x.EncounterId, x.Kind },
                        principalTable: "clinical_records",
                        principalColumns: new[] { "Id", "EncounterId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinical_prescription_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Medicine = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Strength = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Dose = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DoseUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Route = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Duration = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_prescription_items", x => x.Id);
                    table.CheckConstraint("ck_prescription_item_quantity", "\"Quantity\" IS NULL OR \"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_clinical_prescription_items_clinical_records_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "clinical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_encounters_AppointmentId",
                table: "clinical_encounters",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_encounters_DoctorId_PatientId_CreatedAtUtc",
                table: "clinical_encounters",
                columns: new[] { "DoctorId", "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_encounters_PatientId",
                table: "clinical_encounters",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_prescription_items_PrescriptionId_Position",
                table: "clinical_prescription_items",
                columns: new[] { "PrescriptionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_EncounterId_RecordedDate",
                table: "clinical_records",
                columns: new[] { "EncounterId", "RecordedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_PreviousRevisionId",
                table: "clinical_records",
                column: "PreviousRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_PreviousRevisionId_EncounterId_Kind",
                table: "clinical_records",
                columns: new[] { "PreviousRevisionId", "EncounterId", "Kind" });
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_clinical_encounter() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Clinical encounters cannot be deleted' USING ERRCODE = '23514';
                    END IF;

                    IF TG_OP = 'UPDATE' AND (
                        ROW(OLD."PatientId", OLD."DoctorId", OLD."AppointmentId") IS DISTINCT FROM
                        ROW(NEW."PatientId", NEW."DoctorId", NEW."AppointmentId") OR
                        (OLD."FinalizedAtUtc" IS NOT NULL AND
                         ROW(OLD."FinalizedAtUtc", OLD."FinalizedByUserId") IS DISTINCT FROM
                         ROW(NEW."FinalizedAtUtc", NEW."FinalizedByUserId"))
                    ) THEN
                        RAISE EXCEPTION 'Clinical encounter identity and finalization are immutable' USING ERRCODE = '23514';
                    END IF;

                    IF NEW."AppointmentId" IS NOT NULL THEN
                        PERFORM 1 FROM appointments
                            WHERE "Id" = NEW."AppointmentId" AND "PatientId" = NEW."PatientId" AND "DoctorId" = NEW."DoctorId"
                              AND ("Status" = 'CheckedIn' OR (TG_OP = 'UPDATE' AND "Status" = 'Completed'))
                            FOR SHARE;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'Clinical encounter does not match an eligible visit' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER clinical_encounter_guard
                    BEFORE INSERT OR UPDATE OR DELETE ON clinical_encounters
                    FOR EACH ROW EXECUTE FUNCTION guard_clinical_encounter();

                CREATE FUNCTION guard_clinical_record() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Clinical records cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' AND OLD."Status" <> 'Draft' THEN
                        RAISE EXCEPTION 'Signed clinical history cannot be changed' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'UPDATE' AND
                        ROW(OLD."EncounterId", OLD."Kind", OLD."PreviousRevisionId", OLD."AmendmentReason") IS DISTINCT FROM
                        ROW(NEW."EncounterId", NEW."Kind", NEW."PreviousRevisionId", NEW."AmendmentReason")
                    THEN
                        RAISE EXCEPTION 'Clinical revision identity cannot be changed' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."PreviousRevisionId" IS NOT NULL AND TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM clinical_records
                            WHERE "Id" = NEW."PreviousRevisionId" AND "Kind" = NEW."Kind"
                              AND "EncounterId" = NEW."EncounterId" AND "Status" = 'Finalized' FOR SHARE;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'A revision requires a finalized predecessor' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    IF (NEW."Kind" <> 'Prescription' AND
                        (NEW."Content" IS NULL OR btrim(NEW."Content") = '' OR NEW."Status" = 'Voided')) OR
                       (NEW."Kind" = 'Prescription' AND NEW."Status" = 'Finalized' AND NOT EXISTS
                           (SELECT 1 FROM clinical_prescription_items WHERE "PrescriptionId" = NEW."Id"))
                    THEN
                        RAISE EXCEPTION 'Clinical record content is incomplete' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER clinical_record_guard
                    BEFORE INSERT OR UPDATE OR DELETE ON clinical_records
                    FOR EACH ROW EXECUTE FUNCTION guard_clinical_record();

                CREATE FUNCTION guard_clinical_prescription_item() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    parent_id uuid;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        parent_id := OLD."PrescriptionId";
                    ELSE
                        parent_id := NEW."PrescriptionId";
                    END IF;
                    IF TG_OP = 'UPDATE' AND OLD."PrescriptionId" <> NEW."PrescriptionId" THEN
                        RAISE EXCEPTION 'Prescription items cannot be reassigned' USING ERRCODE = '23514';
                    END IF;
                    PERFORM 1 FROM clinical_records
                        WHERE "Id" = parent_id AND "Kind" = 'Prescription' AND "Status" = 'Draft' FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Only draft prescription items may change' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    IF btrim(NEW."Medicine") = '' OR btrim(NEW."Dose") = '' OR btrim(NEW."DoseUnit") = ''
                        OR btrim(NEW."Route") = '' OR btrim(NEW."Frequency") = '' OR btrim(NEW."Duration") = ''
                    THEN
                        RAISE EXCEPTION 'Prescription item is incomplete' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER clinical_prescription_item_guard
                    BEFORE INSERT OR UPDATE OR DELETE ON clinical_prescription_items
                    FOR EACH ROW EXECUTE FUNCTION guard_clinical_prescription_item();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER clinical_prescription_item_guard ON clinical_prescription_items;
                DROP TRIGGER clinical_record_guard ON clinical_records;
                DROP TRIGGER clinical_encounter_guard ON clinical_encounters;
                DROP FUNCTION guard_clinical_prescription_item();
                DROP FUNCTION guard_clinical_record();
                DROP FUNCTION guard_clinical_encounter();
                """);

            migrationBuilder.DropTable(
                name: "clinical_prescription_items");

            migrationBuilder.DropTable(
                name: "clinical_records");

            migrationBuilder.DropTable(
                name: "clinical_encounters");
        }
    }
}
