using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInpatientOutpatientManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_item_source",
                table: "patient_invoice_items");

            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionId",
                table: "patient_invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hospital_wards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospital_wards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hospital_wards_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inpatient_admissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginEncounterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inpatient_admissions", x => x.Id);
                    table.CheckConstraint("ck_admission_approval", "(\"ApprovedAtUtc\" IS NULL) = (\"ApprovedByUserId\" IS NULL) AND (\"Status\" NOT IN ('Approved','Admitted','Discharged') OR \"ApprovedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("ck_admission_cancel", "(\"Status\" = 'Cancelled') = (\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
                    table.CheckConstraint("ck_admission_start", "(\"Status\" IN ('Admitted','Discharged')) = (\"AdmittedAtUtc\" IS NOT NULL AND \"AdmittedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("ck_admission_status", "\"Status\" IN ('Pending','Approved','Admitted','Discharged','Cancelled')");
                    table.ForeignKey(
                        name: "FK_inpatient_admissions_clinical_encounters_OriginEncounterId",
                        column: x => x.OriginEncounterId,
                        principalTable: "clinical_encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inpatient_admissions_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inpatient_admissions_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hospital_beds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospital_beds", x => x.Id);
                    table.CheckConstraint("ck_bed_condition", "\"Condition\" IN ('Ready','Cleaning','Maintenance','Unavailable')");
                    table.ForeignKey(
                        name: "FK_hospital_beds_hospital_wards_WardId",
                        column: x => x.WardId,
                        principalTable: "hospital_wards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inpatient_care_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NurseUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inpatient_care_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inpatient_care_assignments_AspNetUsers_NurseUserId",
                        column: x => x.NurseUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inpatient_care_assignments_inpatient_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "inpatient_admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inpatient_discharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Diagnosis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inpatient_discharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inpatient_discharges_inpatient_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "inpatient_admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inpatient_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectsId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inpatient_observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inpatient_observations_inpatient_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "inpatient_admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inpatient_observations_inpatient_observations_CorrectsId",
                        column: x => x.CorrectsId,
                        principalTable: "inpatient_observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bed_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BedId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bed_allocations", x => x.Id);
                    table.CheckConstraint("ck_bed_allocation_interval", "(\"EndedAtUtc\" IS NULL) = (\"EndedByUserId\" IS NULL) AND (\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_bed_allocations_hospital_beds_BedId",
                        column: x => x.BedId,
                        principalTable: "hospital_beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bed_allocations_inpatient_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "inpatient_admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_billing_admission_claim",
                table: "patient_invoice_items",
                column: "AdmissionId",
                unique: true,
                filter: "\"Claimed\" AND \"AdmissionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_item_source",
                table: "patient_invoice_items",
                sql: "(\"Category\" = 'Admission' AND \"AdmissionId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"AdmissionId\" IS NULL AND ((\"Category\" = 'Consultation' AND \"AppointmentId\" IS NOT NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Laboratory' AND \"LabItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Pharmacy' AND \"DispenseItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL) OR (\"Category\" = 'Other' AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_bed_allocations_BedId_StartedAtUtc",
                table: "bed_allocations",
                columns: new[] { "BedId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_allocation_current_admission",
                table: "bed_allocations",
                column: "AdmissionId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_allocation_current_bed",
                table: "bed_allocations",
                column: "BedId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_hospital_beds_WardId_Label",
                table: "hospital_beds",
                columns: new[] { "WardId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hospital_wards_Code",
                table: "hospital_wards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hospital_wards_DepartmentId",
                table: "hospital_wards",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_admissions_CreatedByUserId_RequestKey",
                table: "inpatient_admissions",
                columns: new[] { "CreatedByUserId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_admissions_DoctorId",
                table: "inpatient_admissions",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_admissions_Number",
                table: "inpatient_admissions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_admissions_OriginEncounterId",
                table: "inpatient_admissions",
                column: "OriginEncounterId",
                unique: true,
                filter: "\"OriginEncounterId\" IS NOT NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_admissions_Status_AdmittedAtUtc",
                table: "inpatient_admissions",
                columns: new[] { "Status", "AdmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_admission_patient_open",
                table: "inpatient_admissions",
                column: "PatientId",
                unique: true,
                filter: "\"Status\" IN ('Pending','Approved','Admitted')");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_care_assignments_AdmissionId_NurseUserId",
                table: "inpatient_care_assignments",
                columns: new[] { "AdmissionId", "NurseUserId" },
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_care_assignments_NurseUserId",
                table: "inpatient_care_assignments",
                column: "NurseUserId");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_discharges_AdmissionId",
                table: "inpatient_discharges",
                column: "AdmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_observations_AdmissionId",
                table: "inpatient_observations",
                column: "AdmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_inpatient_observations_CorrectsId",
                table: "inpatient_observations",
                column: "CorrectsId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_patient_invoice_items_inpatient_admissions_AdmissionId",
                table: "patient_invoice_items",
                column: "AdmissionId",
                principalTable: "inpatient_admissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE bed_allocations ADD CONSTRAINT ex_inpatient_bed_overlap EXCLUDE USING gist
                    ("BedId" WITH =, tstzrange("StartedAtUtc", "EndedAtUtc", '[)') WITH &&);
                ALTER TABLE bed_allocations ADD CONSTRAINT ex_inpatient_stay_overlap EXCLUDE USING gist
                    ("AdmissionId" WITH =, tstzrange("StartedAtUtc", "EndedAtUtc", '[)') WITH &&);
                ALTER TABLE inpatient_observations ADD CONSTRAINT ck_inpatient_observation_measurement CHECK (
                    "Kind" IN ('TemperatureCelsius','PulsePerMinute','RespirationsPerMinute','OxygenSaturationPercent') AND "Value" >= 0
                    AND ("Kind" <> 'OxygenSaturationPercent' OR "Value" <= 100)
                    AND (("CorrectsId" IS NULL AND "CorrectionReason" IS NULL) OR
                         ("CorrectsId" IS NOT NULL AND COALESCE(btrim("CorrectionReason"), '') <> '')));
                ALTER TABLE inpatient_care_assignments ADD CONSTRAINT ck_inpatient_assignment_end CHECK (
                    ("EndedAtUtc" IS NULL) = ("EndedByUserId" IS NULL) AND ("EndedAtUtc" IS NULL OR "EndedAtUtc" >= "AssignedAtUtc"));
                ALTER TABLE inpatient_discharges ADD CONSTRAINT ck_inpatient_discharge_signed CHECK (
                    "Disposition" IN ('Home','Referred','Transferred','Other') AND btrim("Diagnosis") <> '' AND btrim("Summary") <> '');

                CREATE FUNCTION guard_inpatient_history_delete() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Inpatient history cannot be removed or rewritten' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER inpatient_admission_no_delete BEFORE DELETE ON inpatient_admissions
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_history_delete();
                CREATE TRIGGER inpatient_allocation_no_delete BEFORE DELETE ON bed_allocations
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_history_delete();
                CREATE TRIGGER inpatient_assignment_no_delete BEFORE DELETE ON inpatient_care_assignments
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_history_delete();
                CREATE TRIGGER inpatient_observation_immutable BEFORE UPDATE OR DELETE ON inpatient_observations
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_history_delete();
                CREATE TRIGGER inpatient_discharge_immutable BEFORE UPDATE OR DELETE ON inpatient_discharges
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_history_delete();

                CREATE FUNCTION guard_inpatient_admission() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'Pending' OR
                           (NEW."OriginEncounterId" IS NOT NULL AND NOT EXISTS (
                               SELECT 1 FROM clinical_encounters e JOIN appointments a ON a."Id" = e."AppointmentId"
                               WHERE e."Id" = NEW."OriginEncounterId" AND e."PatientId" = NEW."PatientId"
                                 AND e."DoctorId" = NEW."DoctorId" AND a."PatientId" = e."PatientId" AND a."DoctorId" = e."DoctorId"
                                 AND a."Status" IN ('CheckedIn','Completed')
                           ))
                        THEN RAISE EXCEPTION 'Inpatient registration requires an eligible origin' USING ERRCODE = '23514'; END IF;
                        RETURN NEW;
                    END IF;
                    IF ROW(OLD."PatientId", OLD."DoctorId", OLD."OriginEncounterId", OLD."Number", OLD."RequestKey",
                           OLD."Reason", OLD."CreatedAtUtc", OLD."CreatedByUserId") IS DISTINCT FROM
                       ROW(NEW."PatientId", NEW."DoctorId", NEW."OriginEncounterId", NEW."Number", NEW."RequestKey",
                           NEW."Reason", NEW."CreatedAtUtc", NEW."CreatedByUserId") OR
                       OLD."Status" IN ('Cancelled','Discharged') OR
                       NOT ((OLD."Status" = 'Pending' AND NEW."Status" IN ('Approved','Cancelled')) OR
                            (OLD."Status" = 'Approved' AND NEW."Status" IN ('Admitted','Cancelled')) OR
                            (OLD."Status" = 'Admitted' AND NEW."Status" IN ('Admitted','Discharged'))) OR
                       (OLD."ApprovedAtUtc" IS NOT NULL AND
                        ROW(OLD."ApprovedAtUtc", OLD."ApprovedByUserId") IS DISTINCT FROM ROW(NEW."ApprovedAtUtc", NEW."ApprovedByUserId")) OR
                       (OLD."AdmittedAtUtc" IS NOT NULL AND
                        ROW(OLD."AdmittedAtUtc", OLD."AdmittedByUserId") IS DISTINCT FROM ROW(NEW."AdmittedAtUtc", NEW."AdmittedByUserId"))
                    THEN RAISE EXCEPTION 'Invalid inpatient admission transition' USING ERRCODE = '23514'; END IF;
                    IF NEW."Status" = 'Approved' AND NOT EXISTS (
                        SELECT 1 FROM doctors d JOIN patients p ON p."Id" = NEW."PatientId"
                        WHERE d."Id" = NEW."DoctorId" AND d."ApplicationUserId" = NEW."ApprovedByUserId" AND d."IsActive" AND p."IsActive"
                    ) THEN RAISE EXCEPTION 'Approval requires the attending doctor and an active patient' USING ERRCODE = '23514'; END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER inpatient_admission_guard BEFORE INSERT OR UPDATE ON inpatient_admissions
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_admission();

                CREATE FUNCTION guard_inpatient_allocation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM hospital_beds b JOIN hospital_wards w ON w."Id" = b."WardId"
                        WHERE b."Id" = NEW."BedId" AND b."Active" AND w."Active" AND b."Condition" = 'Ready'
                        FOR SHARE OF b,w;
                        IF NOT FOUND THEN RAISE EXCEPTION 'The bed is not available' USING ERRCODE = '23514'; END IF;
                    ELSIF OLD."EndedAtUtc" IS NOT NULL OR NEW."EndedAtUtc" IS NULL OR
                        (to_jsonb(OLD) - ARRAY['EndedAtUtc','EndedByUserId']) IS DISTINCT FROM
                        (to_jsonb(NEW) - ARRAY['EndedAtUtc','EndedByUserId'])
                    THEN RAISE EXCEPTION 'Only a current allocation can be closed' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER inpatient_allocation_guard BEFORE INSERT OR UPDATE ON bed_allocations
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_allocation();

                CREATE FUNCTION check_inpatient_occupancy() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    stay_id uuid;
                    state varchar;
                BEGIN
                    IF TG_TABLE_NAME = 'inpatient_admissions' THEN stay_id := NEW."Id";
                    ELSE stay_id := NEW."AdmissionId"; END IF;
                    SELECT "Status" INTO state FROM inpatient_admissions WHERE "Id" = stay_id;
                    IF ((state = 'Admitted') <> EXISTS (SELECT 1 FROM bed_allocations a WHERE a."AdmissionId" = stay_id AND a."EndedAtUtc" IS NULL))
                        OR ((state = 'Discharged') <> EXISTS (SELECT 1 FROM inpatient_discharges d WHERE d."AdmissionId" = stay_id))
                        OR (state IN ('Pending','Approved','Cancelled') AND EXISTS
                            (SELECT 1 FROM bed_allocations a WHERE a."AdmissionId" = stay_id))
                        OR (state <> 'Admitted' AND EXISTS (SELECT 1 FROM inpatient_care_assignments n
                            WHERE n."AdmissionId" = stay_id AND n."EndedAtUtc" IS NULL))
                    THEN RAISE EXCEPTION 'Admission, bed and discharge records must stay consistent' USING ERRCODE = '23514'; END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER inpatient_stay_consistency AFTER INSERT OR UPDATE ON inpatient_admissions
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_occupancy();
                CREATE CONSTRAINT TRIGGER inpatient_allocation_consistency AFTER INSERT OR UPDATE ON bed_allocations
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_occupancy();
                CREATE CONSTRAINT TRIGGER inpatient_discharge_consistency AFTER INSERT ON inpatient_discharges
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_occupancy();
                CREATE CONSTRAINT TRIGGER inpatient_assignment_consistency AFTER INSERT OR UPDATE ON inpatient_care_assignments
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_occupancy();

                CREATE FUNCTION check_inpatient_bed_availability() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_TABLE_NAME = 'hospital_wards' THEN
                        IF EXISTS (
                            SELECT 1 FROM hospital_wards w JOIN hospital_beds b ON b."WardId" = w."Id"
                            JOIN bed_allocations a ON a."BedId" = b."Id"
                            WHERE w."Id" = NEW."Id" AND NOT w."Active" AND a."EndedAtUtc" IS NULL
                        ) THEN RAISE EXCEPTION 'Occupied ward cannot be deactivated' USING ERRCODE = '23514'; END IF;
                    ELSE
                        IF EXISTS (
                            SELECT 1 FROM hospital_beds b JOIN bed_allocations a ON a."BedId" = b."Id"
                            WHERE b."Id" = NEW."Id" AND (NOT b."Active" OR b."Condition" <> 'Ready') AND a."EndedAtUtc" IS NULL
                        ) THEN RAISE EXCEPTION 'Occupied bed must remain operational' USING ERRCODE = '23514'; END IF;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER inpatient_occupied_ward AFTER UPDATE ON hospital_wards
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_bed_availability();
                CREATE CONSTRAINT TRIGGER inpatient_occupied_bed AFTER UPDATE ON hospital_beds
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_inpatient_bed_availability();

                CREATE FUNCTION guard_inpatient_discharge_signer() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM 1 FROM inpatient_admissions a JOIN doctors d ON d."Id" = a."DoctorId"
                      JOIN "AspNetUsers" u ON u."Id" = d."ApplicationUserId"
                      WHERE a."Id" = NEW."AdmissionId" AND a."Status" IN ('Admitted','Discharged')
                      AND d."IsActive" AND u."IsEnabled" AND u."Id" = NEW."SignedByUserId";
                    IF NOT FOUND THEN RAISE EXCEPTION 'Discharge requires the active attending doctor' USING ERRCODE = '23514'; END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER inpatient_discharge_signer BEFORE INSERT ON inpatient_discharges
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_discharge_signer();

                CREATE FUNCTION guard_inpatient_observation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM inpatient_admissions a WHERE a."Id" = NEW."AdmissionId" AND a."Status" = 'Admitted') OR
                       (NEW."CorrectsId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inpatient_observations o
                           WHERE o."Id" = NEW."CorrectsId" AND o."AdmissionId" = NEW."AdmissionId" AND o."Kind" = NEW."Kind"))
                    THEN RAISE EXCEPTION 'Observation must belong to the active admission' USING ERRCODE = '23514'; END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER inpatient_observation_guard BEFORE INSERT ON inpatient_observations
                    FOR EACH ROW EXECUTE FUNCTION guard_inpatient_observation();
                CREATE OR REPLACE FUNCTION billing_check_invoice_totals() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    invoice_id uuid;
                    header patient_invoices%ROWTYPE;
                BEGIN
                    IF TG_TABLE_NAME = 'patient_invoices' THEN invoice_id := NEW."Id";
                    ELSIF TG_OP = 'DELETE' THEN invoice_id := OLD."InvoiceId";
                    ELSE invoice_id := NEW."InvoiceId"; END IF;
                    SELECT * INTO header FROM patient_invoices WHERE "Id" = invoice_id;
                    IF NOT EXISTS (SELECT 1 FROM patient_invoice_items WHERE "InvoiceId" = invoice_id) OR
                       header."Total" <> (SELECT COALESCE(sum("Total"), 0) FROM patient_invoice_items WHERE "InvoiceId" = invoice_id) OR
                       EXISTS (
                           SELECT 1 FROM patient_invoice_items i
                           JOIN billing_prices p ON p."Id" = i."PriceId"
                           JOIN billing_services s ON s."Id" = p."ServiceId"
                           WHERE i."InvoiceId" = invoice_id AND (
                              i."Claimed" <> (header."Status" = 'Issued' AND i."Category" <> 'Other') OR
                              i."UnitPrice" <> p."Amount" OR i."Category" <> s."Category" OR
                              header."ServiceDate" < p."From" OR header."ServiceDate" >= p."Until" OR
                              (i."AdmissionId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM inpatient_admissions a JOIN inpatient_discharges d ON d."AdmissionId" = a."Id"
                                  WHERE a."Id" = i."AdmissionId" AND a."PatientId" = header."PatientId" AND
                                  a."Status" = 'Discharged' AND i."Quantity" = 1 AND
                                  (d."SignedAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              )) OR
                              (i."AppointmentId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM appointments a WHERE a."Id" = i."AppointmentId" AND
                                  a."PatientId" = header."PatientId" AND a."Status" = 'Completed' AND i."Quantity" = 1 AND
                                  (a."StartAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              )) OR
                              (i."LabItemId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM laboratory_order_items li JOIN laboratory_orders o ON o."Id" = li."OrderId"
                                  JOIN clinical_encounters e ON e."Id" = o."EncounterId"
                                  WHERE li."Id" = i."LabItemId" AND e."PatientId" = header."PatientId" AND
                                  o."Status" = 'Completed' AND li."Code" = s."SourceCode" AND i."Quantity" = 1 AND
                                  (o."CreatedAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              )) OR
                              (i."DispenseItemId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM prescription_dispense_items pi JOIN prescription_dispenses d ON d."Id" = pi."DispenseId"
                                  JOIN clinical_records r ON r."Id" = d."PrescriptionId"
                                  JOIN clinical_encounters e ON e."Id" = r."EncounterId"
                                  WHERE pi."Id" = i."DispenseItemId" AND e."PatientId" = header."PatientId" AND
                                  pi."Quantity" = i."Quantity" AND
                                  (d."OccurredAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              ))
                           )
                       )
                    THEN
                        RAISE EXCEPTION 'Invoice lines must reconcile to authorized sources, prices and totals' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION billing_check_invoice_totals() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    invoice_id uuid;
                    header patient_invoices%ROWTYPE;
                BEGIN
                    IF TG_TABLE_NAME = 'patient_invoices' THEN invoice_id := NEW."Id";
                    ELSIF TG_OP = 'DELETE' THEN invoice_id := OLD."InvoiceId";
                    ELSE invoice_id := NEW."InvoiceId"; END IF;
                    SELECT * INTO header FROM patient_invoices WHERE "Id" = invoice_id;
                    IF NOT EXISTS (SELECT 1 FROM patient_invoice_items WHERE "InvoiceId" = invoice_id) OR
                       header."Total" <> (SELECT COALESCE(sum("Total"), 0) FROM patient_invoice_items WHERE "InvoiceId" = invoice_id) OR
                       EXISTS (
                           SELECT 1 FROM patient_invoice_items i
                           JOIN billing_prices p ON p."Id" = i."PriceId"
                           JOIN billing_services s ON s."Id" = p."ServiceId"
                           WHERE i."InvoiceId" = invoice_id AND (
                              i."Claimed" <> (header."Status" = 'Issued' AND i."Category" <> 'Other') OR
                              i."UnitPrice" <> p."Amount" OR i."Category" <> s."Category" OR
                              header."ServiceDate" < p."From" OR header."ServiceDate" >= p."Until" OR
                              (i."AppointmentId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM appointments a WHERE a."Id" = i."AppointmentId" AND
                                  a."PatientId" = header."PatientId" AND a."Status" = 'Completed' AND i."Quantity" = 1 AND
                                  (a."StartAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              )) OR
                              (i."LabItemId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM laboratory_order_items li JOIN laboratory_orders o ON o."Id" = li."OrderId"
                                  JOIN clinical_encounters e ON e."Id" = o."EncounterId"
                                  WHERE li."Id" = i."LabItemId" AND e."PatientId" = header."PatientId" AND
                                  o."Status" = 'Completed' AND li."Code" = s."SourceCode" AND i."Quantity" = 1 AND
                                  (o."CreatedAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              )) OR
                              (i."DispenseItemId" IS NOT NULL AND NOT EXISTS (
                                  SELECT 1 FROM prescription_dispense_items pi JOIN prescription_dispenses d ON d."Id" = pi."DispenseId"
                                  JOIN clinical_records r ON r."Id" = d."PrescriptionId"
                                  JOIN clinical_encounters e ON e."Id" = r."EncounterId"
                                  WHERE pi."Id" = i."DispenseItemId" AND e."PatientId" = header."PatientId" AND
                                  pi."Quantity" = i."Quantity" AND
                                  (d."OccurredAtUtc" AT TIME ZONE 'Asia/Colombo')::date = header."ServiceDate"
                              ))
                           )
                       )
                    THEN
                        RAISE EXCEPTION 'Invoice lines must reconcile to authorized sources, prices and totals' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                DROP TRIGGER inpatient_discharge_signer ON inpatient_discharges;
                DROP TRIGGER inpatient_occupied_bed ON hospital_beds;
                DROP TRIGGER inpatient_occupied_ward ON hospital_wards;
                DROP FUNCTION guard_inpatient_discharge_signer();
                DROP FUNCTION check_inpatient_bed_availability();
                DROP TRIGGER inpatient_observation_guard ON inpatient_observations;
                DROP TRIGGER inpatient_assignment_consistency ON inpatient_care_assignments;
                DROP TRIGGER inpatient_discharge_consistency ON inpatient_discharges;
                DROP TRIGGER inpatient_allocation_consistency ON bed_allocations;
                DROP TRIGGER inpatient_stay_consistency ON inpatient_admissions;
                DROP TRIGGER inpatient_allocation_guard ON bed_allocations;
                DROP TRIGGER inpatient_admission_guard ON inpatient_admissions;
                DROP TRIGGER inpatient_discharge_immutable ON inpatient_discharges;
                DROP TRIGGER inpatient_observation_immutable ON inpatient_observations;
                DROP TRIGGER inpatient_assignment_no_delete ON inpatient_care_assignments;
                DROP TRIGGER inpatient_allocation_no_delete ON bed_allocations;
                DROP TRIGGER inpatient_admission_no_delete ON inpatient_admissions;
                DROP FUNCTION guard_inpatient_observation();
                DROP FUNCTION check_inpatient_occupancy();
                DROP FUNCTION guard_inpatient_allocation();
                DROP FUNCTION guard_inpatient_admission();
                DROP FUNCTION guard_inpatient_history_delete();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_patient_invoice_items_inpatient_admissions_AdmissionId",
                table: "patient_invoice_items");

            migrationBuilder.DropTable(
                name: "bed_allocations");

            migrationBuilder.DropTable(
                name: "inpatient_care_assignments");

            migrationBuilder.DropTable(
                name: "inpatient_discharges");

            migrationBuilder.DropTable(
                name: "inpatient_observations");

            migrationBuilder.DropTable(
                name: "hospital_beds");

            migrationBuilder.DropTable(
                name: "inpatient_admissions");

            migrationBuilder.DropTable(
                name: "hospital_wards");

            migrationBuilder.DropIndex(
                name: "ux_billing_admission_claim",
                table: "patient_invoice_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_item_source",
                table: "patient_invoice_items");

            migrationBuilder.DropColumn(
                name: "AdmissionId",
                table: "patient_invoice_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_item_source",
                table: "patient_invoice_items",
                sql: "(\"Category\" = 'Consultation' AND \"AppointmentId\" IS NOT NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Laboratory' AND \"LabItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Pharmacy' AND \"DispenseItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL) OR (\"Category\" = 'Other' AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL)");
        }
    }
}
