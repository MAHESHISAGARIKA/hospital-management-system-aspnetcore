using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLaboratoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "laboratory_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_orders", x => x.Id);
                    table.CheckConstraint("ck_lab_order_cancel", "(\"Status\" = 'Cancelled') = (\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
                    table.CheckConstraint("ck_lab_order_status", "\"Status\" IN ('Requested','SampleCollected','InProgress','ResultsEntered','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_laboratory_orders_clinical_encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "clinical_encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "laboratory_tests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Specimen = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_tests", x => x.Id);
                    table.CheckConstraint("ck_lab_test_kind", "\"Kind\" IN ('Quantitative','Qualitative')");
                });

            migrationBuilder.CreateTable(
                name: "laboratory_order_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Specimen = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_order_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_laboratory_order_items_laboratory_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "laboratory_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_laboratory_order_items_laboratory_tests_TestId",
                        column: x => x.TestId,
                        principalTable: "laboratory_tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "laboratory_samples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Accession = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_samples", x => x.Id);
                    table.UniqueConstraint("AK_laboratory_samples_Id_ItemId", x => new { x.Id, x.ItemId });
                    table.CheckConstraint("ck_lab_sample_rejected", "(\"Status\" = 'Rejected') = (\"RejectedAtUtc\" IS NOT NULL AND \"RejectedByUserId\" IS NOT NULL AND \"RejectionReason\" IS NOT NULL)");
                    table.CheckConstraint("ck_lab_sample_status", "\"Status\" IN ('Collected','Received','Processing','Rejected')");
                    table.ForeignKey(
                        name: "FK_laboratory_samples_laboratory_order_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "laboratory_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "laboratory_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SampleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmendmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    QualitativeValue = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifierName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_results", x => x.Id);
                    table.UniqueConstraint("AK_laboratory_results_Id_ItemId", x => new { x.Id, x.ItemId });
                    table.CheckConstraint("ck_lab_result_amendment", "(\"PreviousId\" IS NULL) = (\"AmendmentReason\" IS NULL)");
                    table.CheckConstraint("ck_lab_result_status", "\"Status\" IN ('Draft','Submitted','Verified')");
                    table.CheckConstraint("ck_lab_result_submitted", "(\"Status\" <> 'Draft') = (\"SubmittedAtUtc\" IS NOT NULL AND \"SubmittedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("ck_lab_result_value", "(\"Value\" IS NOT NULL) <> (\"QualitativeValue\" IS NOT NULL)");
                    table.CheckConstraint("ck_lab_result_verified", "(\"Status\" = 'Verified') = (\"VerifiedAtUtc\" IS NOT NULL AND \"VerifiedByUserId\" IS NOT NULL AND \"VerifierName\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_laboratory_results_laboratory_order_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "laboratory_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_laboratory_results_laboratory_results_PreviousId_ItemId",
                        columns: x => new { x.PreviousId, x.ItemId },
                        principalTable: "laboratory_results",
                        principalColumns: new[] { "Id", "ItemId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_laboratory_results_laboratory_samples_SampleId_ItemId",
                        columns: x => new { x.SampleId, x.ItemId },
                        principalTable: "laboratory_samples",
                        principalColumns: new[] { "Id", "ItemId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_order_items_OrderId_TestId",
                table: "laboratory_order_items",
                columns: new[] { "OrderId", "TestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_order_items_TestId",
                table: "laboratory_order_items",
                column: "TestId");

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_orders_EncounterId_RequestKey",
                table: "laboratory_orders",
                columns: new[] { "EncounterId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_orders_Number",
                table: "laboratory_orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_orders_Status_CreatedAtUtc",
                table: "laboratory_orders",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_results_ItemId",
                table: "laboratory_results",
                column: "ItemId",
                unique: true,
                filter: "\"PreviousId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_results_PreviousId",
                table: "laboratory_results",
                column: "PreviousId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_results_PreviousId_ItemId",
                table: "laboratory_results",
                columns: new[] { "PreviousId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_results_SampleId_ItemId",
                table: "laboratory_results",
                columns: new[] { "SampleId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_samples_Accession",
                table: "laboratory_samples",
                column: "Accession",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_samples_ItemId",
                table: "laboratory_samples",
                column: "ItemId",
                unique: true,
                filter: "\"Status\" <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_tests_Code",
                table: "laboratory_tests",
                column: "Code",
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION guard_laboratory_order() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Laboratory orders cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM clinical_encounters e
                        JOIN appointments a ON a."Id" = e."AppointmentId"
                        JOIN doctors d ON d."Id" = e."DoctorId"
                        JOIN patients p ON p."Id" = e."PatientId"
                        WHERE e."Id" = NEW."EncounterId" AND e."FinalizedAtUtc" IS NULL
                          AND a."DoctorId" = e."DoctorId" AND a."PatientId" = e."PatientId"
                          AND a."Status" IN ('CheckedIn','Completed') AND d."IsActive" AND p."IsActive"
                          AND d."ApplicationUserId" = NEW."CreatedByUserId"
                        FOR SHARE OF e, a, d, p;
                        IF NOT FOUND OR NEW."Status" <> 'Requested' THEN
                            RAISE EXCEPTION 'Laboratory order requires an eligible encounter' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF OLD."Status" = 'Cancelled' OR
                           (OLD."Status" = 'Completed' AND NEW."Status" <> 'Completed') OR
                           ROW(OLD."EncounterId", OLD."RequestKey", OLD."Number", OLD."Instructions", OLD."CreatedByUserId", OLD."CreatedAtUtc")
                           IS DISTINCT FROM
                           ROW(NEW."EncounterId", NEW."RequestKey", NEW."Number", NEW."Instructions", NEW."CreatedByUserId", NEW."CreatedAtUtc")
                        THEN
                            RAISE EXCEPTION 'The laboratory order identity is immutable' USING ERRCODE = '23514';
                        END IF;
                        IF NEW."Status" = 'Cancelled' AND (
                            OLD."Status" <> 'Requested' OR btrim(NEW."CancellationReason") = '' OR EXISTS (
                                SELECT 1 FROM laboratory_order_items i JOIN laboratory_samples s ON s."ItemId" = i."Id"
                                WHERE i."OrderId" = NEW."Id"
                            )
                        ) THEN
                            RAISE EXCEPTION 'Cancellation is limited to orders without collection history' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER laboratory_order_guard BEFORE INSERT OR UPDATE OR DELETE ON laboratory_orders
                    FOR EACH ROW EXECUTE FUNCTION guard_laboratory_order();

                CREATE FUNCTION validate_laboratory_completion() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM laboratory_order_items i WHERE i."OrderId" = NEW."Id") OR
                       (NEW."Status" = 'Completed' AND EXISTS (
                            SELECT 1 FROM laboratory_order_items i WHERE i."OrderId" = NEW."Id" AND NOT EXISTS (
                                SELECT 1 FROM laboratory_results r WHERE r."ItemId" = i."Id" AND r."Status" = 'Verified'
                            )
                       ))
                    THEN
                        RAISE EXCEPTION 'Laboratory order or release is incomplete' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE CONSTRAINT TRIGGER laboratory_completion_guard
                    AFTER INSERT OR UPDATE ON laboratory_orders DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION validate_laboratory_completion();

                CREATE FUNCTION guard_laboratory_item() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'Laboratory order item snapshots cannot change' USING ERRCODE = '23514';
                    END IF;
                    PERFORM 1 FROM laboratory_tests t WHERE t."Id" = NEW."TestId" AND t."Active"
                        AND ROW(t."Code", t."Name", t."Specimen", t."Unit", t."Kind") IS NOT DISTINCT FROM
                            ROW(NEW."Code", NEW."Name", NEW."Specimen", NEW."Unit", NEW."Kind") FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Laboratory item requires an active matching catalog test' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER laboratory_item_guard BEFORE INSERT OR UPDATE OR DELETE ON laboratory_order_items
                    FOR EACH ROW EXECUTE FUNCTION guard_laboratory_item();

                CREATE FUNCTION guard_laboratory_sample() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Laboratory samples cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        PERFORM 1 FROM laboratory_order_items i JOIN laboratory_orders o ON o."Id" = i."OrderId"
                          WHERE i."Id" = NEW."ItemId" AND o."Status" <> 'Cancelled' FOR SHARE OF o;
                        IF NOT FOUND OR NEW."Status" <> 'Collected' THEN
                            RAISE EXCEPTION 'A sample begins in collection for an active order' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF ROW(OLD."ItemId", OLD."Accession", OLD."CreatedByUserId", OLD."CreatedAtUtc") IS DISTINCT FROM
                           ROW(NEW."ItemId", NEW."Accession", NEW."CreatedByUserId", NEW."CreatedAtUtc") OR
                           NOT ((OLD."Status" = 'Collected' AND NEW."Status" = 'Received') OR
                                (OLD."Status" = 'Received' AND NEW."Status" = 'Processing') OR
                                (OLD."Status" <> 'Rejected' AND NEW."Status" = 'Rejected' AND
                                 btrim(NEW."RejectionReason") <> '' AND NOT EXISTS
                                    (SELECT 1 FROM laboratory_results WHERE "SampleId" = OLD."Id")))
                        THEN
                            RAISE EXCEPTION 'Invalid laboratory sample transition' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    IF (NEW."Status" IN ('Received','Processing') AND
                        (NEW."ReceivedAtUtc" IS NULL OR NEW."ReceivedByUserId" IS NULL)) OR
                       (NEW."Status" = 'Processing' AND (NEW."ProcessingAtUtc" IS NULL OR NEW."ProcessingByUserId" IS NULL))
                    THEN
                        RAISE EXCEPTION 'Sample transition requires custody metadata' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER laboratory_sample_guard BEFORE INSERT OR UPDATE OR DELETE ON laboratory_samples
                    FOR EACH ROW EXECUTE FUNCTION guard_laboratory_sample();

                CREATE FUNCTION guard_laboratory_result() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE expected_kind varchar;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Laboratory result history cannot be deleted' USING ERRCODE = '23514';
                    END IF;
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'Draft' OR (NEW."PreviousId" IS NOT NULL AND
                            (btrim(NEW."AmendmentReason") = '' OR NOT EXISTS (
                                SELECT 1 FROM laboratory_results r WHERE r."Id" = NEW."PreviousId"
                                  AND r."ItemId" = NEW."ItemId" AND r."SampleId" = NEW."SampleId" AND r."Status" = 'Verified'
                            )))
                        THEN
                            RAISE EXCEPTION 'Laboratory result must begin as a valid draft' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF OLD."Status" = 'Verified' OR
                           (OLD."Status" = 'Submitted' AND (
                             NEW."Status" NOT IN ('Draft','Verified') OR
                             ROW(OLD."Value", OLD."QualitativeValue", OLD."Unit", OLD."Note") IS DISTINCT FROM
                             ROW(NEW."Value", NEW."QualitativeValue", NEW."Unit", NEW."Note") OR
                             (NEW."Status" = 'Verified' AND
                              ROW(OLD."SubmittedByUserId", OLD."SubmittedAtUtc") IS DISTINCT FROM
                              ROW(NEW."SubmittedByUserId", NEW."SubmittedAtUtc")))) OR
                           (OLD."Status" = 'Draft' AND NEW."Status" = 'Verified') OR
                           ROW(OLD."ItemId", OLD."SampleId", OLD."PreviousId", OLD."AmendmentReason", OLD."CreatedByUserId", OLD."CreatedAtUtc")
                           IS DISTINCT FROM
                           ROW(NEW."ItemId", NEW."SampleId", NEW."PreviousId", NEW."AmendmentReason", NEW."CreatedByUserId", NEW."CreatedAtUtc")
                        THEN
                            RAISE EXCEPTION 'Laboratory result transition would alter signed history' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    SELECT i."Kind" INTO expected_kind FROM laboratory_order_items i
                      JOIN laboratory_samples s ON s."ItemId" = i."Id"
                      WHERE i."Id" = NEW."ItemId" AND s."Id" = NEW."SampleId" AND s."Status" = 'Processing'
                        AND NEW."Unit" IS NOT DISTINCT FROM i."Unit" FOR SHARE OF s;
                    IF NOT FOUND OR (expected_kind = 'Quantitative') <> (NEW."Value" IS NOT NULL)
                      OR (expected_kind = 'Qualitative' AND btrim(NEW."QualitativeValue") = '')
                    THEN
                        RAISE EXCEPTION 'Laboratory result must match its processing specimen and requested test' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER laboratory_result_guard BEFORE INSERT OR UPDATE OR DELETE ON laboratory_results
                    FOR EACH ROW EXECUTE FUNCTION guard_laboratory_result();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER laboratory_result_guard ON laboratory_results;
                DROP TRIGGER laboratory_sample_guard ON laboratory_samples;
                DROP TRIGGER laboratory_item_guard ON laboratory_order_items;
                DROP TRIGGER laboratory_completion_guard ON laboratory_orders;
                DROP TRIGGER laboratory_order_guard ON laboratory_orders;
                DROP FUNCTION guard_laboratory_result();
                DROP FUNCTION guard_laboratory_sample();
                DROP FUNCTION guard_laboratory_item();
                DROP FUNCTION validate_laboratory_completion();
                DROP FUNCTION guard_laboratory_order();
                """);

            migrationBuilder.DropTable(
                name: "laboratory_results");

            migrationBuilder.DropTable(
                name: "laboratory_samples");

            migrationBuilder.DropTable(
                name: "laboratory_order_items");

            migrationBuilder.DropTable(
                name: "laboratory_orders");

            migrationBuilder.DropTable(
                name: "laboratory_tests");
        }
    }
}
