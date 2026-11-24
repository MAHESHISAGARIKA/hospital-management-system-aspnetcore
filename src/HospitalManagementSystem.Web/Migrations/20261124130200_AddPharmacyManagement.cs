using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPharmacyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispenseUnit",
                table: "clinical_prescription_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_clinical_prescription_items_Id_PrescriptionId",
                table: "clinical_prescription_items",
                columns: new[] { "Id", "PrescriptionId" });

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GenericName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    BrandName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Strength = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Form = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Manufacturer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReorderThreshold = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicines", x => x.Id);
                    table.CheckConstraint("ck_medicine_threshold", "\"ReorderThreshold\" >= 0 AND (\"Unit\" IN ('Millilitre','Gram') OR trunc(\"ReorderThreshold\") = \"ReorderThreshold\")");
                });

            migrationBuilder.CreateTable(
                name: "prescription_dispenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPartial = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescription_dispenses", x => x.Id);
                    table.UniqueConstraint("AK_prescription_dispenses_Id_PrescriptionId", x => new { x.Id, x.PrescriptionId });
                    table.ForeignKey(
                        name: "FK_prescription_dispenses_clinical_records_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "clinical_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medicine_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Supplier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Available = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Expiry = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_batches", x => x.Id);
                    table.CheckConstraint("ck_batch_balance", "\"Available\" >= 0 AND \"ReceivedQuantity\" > 0 AND (\"Unit\" IN ('Millilitre','Gram') OR (trunc(\"Available\") = \"Available\" AND trunc(\"ReceivedQuantity\") = \"ReceivedQuantity\"))");
                    table.CheckConstraint("ck_batch_dates", "\"Expiry\" >= \"ReceivedDate\"");
                    table.CheckConstraint("ck_batch_status", "\"Status\" IN ('Available','Quarantined','Recalled')");
                    table.ForeignKey(
                        name: "FK_medicine_batches_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescription_dispense_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DispenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FefoOverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prescription_dispense_items", x => x.Id);
                    table.CheckConstraint("ck_dispense_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_prescription_dispense_items_clinical_prescription_items_Pre~",
                        columns: x => new { x.PrescriptionItemId, x.PrescriptionId },
                        principalTable: "clinical_prescription_items",
                        principalColumns: new[] { "Id", "PrescriptionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prescription_dispense_items_medicine_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "medicine_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prescription_dispense_items_prescription_dispenses_Dispense~",
                        columns: x => new { x.DispenseId, x.PrescriptionId },
                        principalTable: "prescription_dispenses",
                        principalColumns: new[] { "Id", "PrescriptionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pharmacy_stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    DispenseItemId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pharmacy_stock_movements", x => x.Id);
                    table.CheckConstraint("ck_stock_movement_quantity", "(\"Kind\" IN ('Quarantined','Recalled') AND \"Quantity\" = 0) OR (\"Kind\" NOT IN ('Quarantined','Recalled') AND \"Quantity\" > 0)");
                    table.ForeignKey(
                        name: "FK_pharmacy_stock_movements_medicine_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "medicine_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pharmacy_stock_movements_prescription_dispense_items_Dispen~",
                        column: x => x.DispenseItemId,
                        principalTable: "prescription_dispense_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medicine_batches_MedicineId_Number",
                table: "medicine_batches",
                columns: new[] { "MedicineId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicine_batches_Status_Expiry",
                table: "medicine_batches",
                columns: new[] { "Status", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_medicines_Code",
                table: "medicines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_GenericName",
                table: "medicines",
                column: "GenericName");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_stock_movements_ActorId_RequestKey",
                table: "pharmacy_stock_movements",
                columns: new[] { "ActorId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_stock_movements_BatchId_OccurredAtUtc",
                table: "pharmacy_stock_movements",
                columns: new[] { "BatchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_stock_movements_DispenseItemId",
                table: "pharmacy_stock_movements",
                column: "DispenseItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispense_items_BatchId",
                table: "prescription_dispense_items",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispense_items_DispenseId_PrescriptionId",
                table: "prescription_dispense_items",
                columns: new[] { "DispenseId", "PrescriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispense_items_DispenseId_PrescriptionItemId_B~",
                table: "prescription_dispense_items",
                columns: new[] { "DispenseId", "PrescriptionItemId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispense_items_PrescriptionItemId_Prescription~",
                table: "prescription_dispense_items",
                columns: new[] { "PrescriptionItemId", "PrescriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispenses_ActorId_RequestKey",
                table: "prescription_dispenses",
                columns: new[] { "ActorId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispenses_Number",
                table: "prescription_dispenses",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prescription_dispenses_PrescriptionId_OccurredAtUtc",
                table: "prescription_dispenses",
                columns: new[] { "PrescriptionId", "OccurredAtUtc" });
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_pharmacy_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Pharmacy history is append-only' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER pharmacy_movement_immutable BEFORE UPDATE OR DELETE ON pharmacy_stock_movements
                    FOR EACH ROW EXECUTE FUNCTION prevent_pharmacy_history_change();
                CREATE TRIGGER pharmacy_dispense_immutable BEFORE UPDATE OR DELETE ON prescription_dispenses
                    FOR EACH ROW EXECUTE FUNCTION prevent_pharmacy_history_change();
                CREATE TRIGGER pharmacy_dispense_line_immutable BEFORE UPDATE OR DELETE ON prescription_dispense_items
                    FOR EACH ROW EXECUTE FUNCTION prevent_pharmacy_history_change();
                CREATE TRIGGER pharmacy_batch_no_delete BEFORE DELETE ON medicine_batches
                    FOR EACH ROW EXECUTE FUNCTION prevent_pharmacy_history_change();

                CREATE FUNCTION guard_pharmacy_batch() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(OLD."MedicineId", OLD."Number", OLD."Unit", OLD."Expiry", OLD."ReceivedDate",
                           OLD."ReceivedQuantity", OLD."ReceivedByUserId", OLD."CreatedAtUtc", OLD."Supplier", OLD."Notes")
                       IS DISTINCT FROM
                       ROW(NEW."MedicineId", NEW."Number", NEW."Unit", NEW."Expiry", NEW."ReceivedDate",
                           NEW."ReceivedQuantity", NEW."ReceivedByUserId", NEW."CreatedAtUtc", NEW."Supplier", NEW."Notes") OR
                       (OLD."Status" = 'Recalled' AND NEW."Status" <> 'Recalled') OR
                       (OLD."Status" = 'Quarantined' AND NEW."Status" = 'Available')
                    THEN
                        RAISE EXCEPTION 'Batch identity and stock holds cannot be rewritten' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER pharmacy_batch_guard BEFORE UPDATE ON medicine_batches
                    FOR EACH ROW EXECUTE FUNCTION guard_pharmacy_batch();

                CREATE FUNCTION guard_pharmacy_dispense() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE encounter_id uuid;
                BEGIN
                    SELECT r."EncounterId" INTO encounter_id FROM clinical_records r WHERE r."Id" = NEW."PrescriptionId";
                    PERFORM 1 FROM clinical_encounters WHERE "Id" = encounter_id FOR UPDATE;
                    PERFORM 1 FROM clinical_records r JOIN clinical_encounters e ON e."Id" = r."EncounterId"
                      JOIN patients p ON p."Id" = e."PatientId" JOIN doctors d ON d."Id" = e."DoctorId"
                      JOIN appointments a ON a."Id" = e."AppointmentId"
                      WHERE r."Id" = NEW."PrescriptionId" AND r."Kind" = 'Prescription' AND r."Status" = 'Finalized'
                        AND p."IsActive" AND d."IsActive" AND a."PatientId" = e."PatientId" AND a."DoctorId" = e."DoctorId"
                        AND a."Status" IN ('CheckedIn','Completed')
                        AND NOT EXISTS (SELECT 1 FROM clinical_records n WHERE n."PreviousRevisionId" = r."Id" AND n."Status" <> 'Draft')
                      FOR SHARE OF p, d, a;
                    IF NOT FOUND OR EXISTS (
                        WITH RECURSIVE predecessors AS (
                            SELECT r."PreviousRevisionId" AS id FROM clinical_records r WHERE r."Id" = NEW."PrescriptionId"
                            UNION
                            SELECT r."PreviousRevisionId" FROM clinical_records r JOIN predecessors p ON p.id = r."Id"
                        )
                        SELECT 1 FROM predecessors p JOIN prescription_dispenses d ON d."PrescriptionId" = p.id
                    )
                    THEN
                        RAISE EXCEPTION 'Prescription is not eligible for dispensing' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER pharmacy_dispense_guard BEFORE INSERT ON prescription_dispenses
                    FOR EACH ROW EXECUTE FUNCTION guard_pharmacy_dispense();

                CREATE FUNCTION guard_pharmacy_line() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM 1 FROM clinical_prescription_items i
                      JOIN medicine_batches b ON b."Id" = NEW."BatchId"
                      JOIN medicines m ON m."Id" = b."MedicineId"
                      WHERE i."Id" = NEW."PrescriptionItemId" AND i."PrescriptionId" = NEW."PrescriptionId"
                        AND i."Quantity" IS NOT NULL AND i."DispenseUnit" = b."Unit" AND m."Unit" = b."Unit"
                        AND m."IsActive" AND b."Status" = 'Available' AND b."Expiry" >= (clock_timestamp() AT TIME ZONE 'Asia/Colombo')::date
                        AND (upper(btrim(i."Medicine")) = upper(btrim(m."GenericName")) OR upper(btrim(i."Medicine")) = upper(btrim(m."BrandName")))
                        AND upper(btrim(i."Strength")) = upper(btrim(m."Strength"))
                        AND (b."Unit" IN ('Gram','Millilitre') OR trunc(NEW."Quantity") = NEW."Quantity")
                        AND i."Quantity" >= NEW."Quantity" + COALESCE((
                            SELECT sum(l."Quantity") FROM prescription_dispense_items l WHERE l."PrescriptionItemId" = i."Id"
                        ), 0)
                      FOR SHARE OF m, b;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Dispense line does not match eligible stock or authorized quantity' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER pharmacy_line_guard BEFORE INSERT ON prescription_dispense_items
                    FOR EACH ROW EXECUTE FUNCTION guard_pharmacy_line();

                CREATE FUNCTION guard_pharmacy_movement() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM 1 FROM medicine_batches b WHERE b."Id" = NEW."BatchId" AND b."Unit" = NEW."Unit" FOR UPDATE;
                    IF NOT FOUND OR NEW."Kind" NOT IN ('Received','Dispensed','Increase','Decrease','Quarantined','Recalled','Removed') OR
                       (NEW."Unit" NOT IN ('Gram','Millilitre') AND trunc(NEW."Quantity") <> NEW."Quantity") OR
                       (NEW."Kind" NOT IN ('Received','Dispensed') AND COALESCE(btrim(NEW."Reason"), '') = '') OR
                       ((NEW."Kind" = 'Dispensed') <> (NEW."DispenseItemId" IS NOT NULL)) OR
                       (NEW."DispenseItemId" IS NOT NULL AND NOT EXISTS (
                           SELECT 1 FROM prescription_dispense_items l
                           WHERE l."Id" = NEW."DispenseItemId" AND l."BatchId" = NEW."BatchId" AND l."Quantity" = NEW."Quantity"
                       ))
                    THEN
                        RAISE EXCEPTION 'Movement must match the stock batch and its dispense line' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER pharmacy_movement_guard BEFORE INSERT ON pharmacy_stock_movements
                    FOR EACH ROW EXECUTE FUNCTION guard_pharmacy_movement();

                CREATE FUNCTION check_pharmacy_balance() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE batch_id uuid;
                BEGIN
                    IF TG_TABLE_NAME = 'medicine_batches' THEN batch_id := NEW."Id";
                    ELSE batch_id := NEW."BatchId"; END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM medicine_batches b JOIN pharmacy_stock_movements m ON m."BatchId" = b."Id"
                        WHERE b."Id" = batch_id GROUP BY b."Id"
                        HAVING count(*) FILTER (WHERE m."Kind" = 'Received') = 1
                          AND b."ReceivedQuantity" = sum(m."Quantity") FILTER (WHERE m."Kind" = 'Received')
                          AND b."Available" = sum(CASE WHEN m."Kind" IN ('Received','Increase') THEN m."Quantity"
                              WHEN m."Kind" IN ('Dispensed','Decrease','Removed') THEN -m."Quantity" ELSE 0 END)
                    ) THEN
                        RAISE EXCEPTION 'Stock balance must reconcile to its movement ledger' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER pharmacy_balance_batch AFTER INSERT OR UPDATE ON medicine_batches
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_pharmacy_balance();
                CREATE CONSTRAINT TRIGGER pharmacy_balance_movement AFTER INSERT ON pharmacy_stock_movements
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_pharmacy_balance();

                CREATE FUNCTION check_pharmacy_dispense_complete() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_TABLE_NAME = 'prescription_dispenses' THEN
                        IF NOT EXISTS (SELECT 1 FROM prescription_dispense_items l WHERE l."DispenseId" = NEW."Id") THEN
                            RAISE EXCEPTION 'Dispense must have at least one line' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF NOT EXISTS (SELECT 1 FROM pharmacy_stock_movements m WHERE m."DispenseItemId" = NEW."Id") THEN
                            RAISE EXCEPTION 'Dispense line requires a movement' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER pharmacy_dispense_complete AFTER INSERT ON prescription_dispenses
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_pharmacy_dispense_complete();
                CREATE CONSTRAINT TRIGGER pharmacy_line_complete AFTER INSERT ON prescription_dispense_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION check_pharmacy_dispense_complete();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER pharmacy_dispense_complete ON prescription_dispenses;
                DROP TRIGGER pharmacy_line_complete ON prescription_dispense_items;
                DROP TRIGGER pharmacy_balance_batch ON medicine_batches;
                DROP TRIGGER pharmacy_balance_movement ON pharmacy_stock_movements;
                DROP TRIGGER pharmacy_movement_guard ON pharmacy_stock_movements;
                DROP TRIGGER pharmacy_line_guard ON prescription_dispense_items;
                DROP TRIGGER pharmacy_dispense_guard ON prescription_dispenses;
                DROP TRIGGER pharmacy_batch_guard ON medicine_batches;
                DROP TRIGGER pharmacy_movement_immutable ON pharmacy_stock_movements;
                DROP TRIGGER pharmacy_dispense_immutable ON prescription_dispenses;
                DROP TRIGGER pharmacy_dispense_line_immutable ON prescription_dispense_items;
                DROP TRIGGER pharmacy_batch_no_delete ON medicine_batches;
                DROP FUNCTION check_pharmacy_dispense_complete();
                DROP FUNCTION check_pharmacy_balance();
                DROP FUNCTION guard_pharmacy_movement();
                DROP FUNCTION guard_pharmacy_line();
                DROP FUNCTION guard_pharmacy_dispense();
                DROP FUNCTION guard_pharmacy_batch();
                DROP FUNCTION prevent_pharmacy_history_change();
                """);

            migrationBuilder.DropTable(
                name: "pharmacy_stock_movements");

            migrationBuilder.DropTable(
                name: "prescription_dispense_items");

            migrationBuilder.DropTable(
                name: "medicine_batches");

            migrationBuilder.DropTable(
                name: "prescription_dispenses");

            migrationBuilder.DropTable(
                name: "medicines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_clinical_prescription_items_Id_PrescriptionId",
                table: "clinical_prescription_items");

            migrationBuilder.DropColumn(
                name: "DispenseUnit",
                table: "clinical_prescription_items");
        }
    }
}
