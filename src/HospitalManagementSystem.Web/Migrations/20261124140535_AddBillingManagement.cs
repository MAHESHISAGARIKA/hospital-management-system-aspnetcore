using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagementSystem.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patient_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    ReplacesId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PatientLabel = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: true),
                    PatientReference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_invoices", x => x.Id);
                    table.CheckConstraint("ck_invoice_issue", "(\"Status\" <> 'Draft') = (\"Number\" IS NOT NULL AND \"IssuedAtUtc\" IS NOT NULL AND \"IssuedByUserId\" IS NOT NULL AND \"PatientLabel\" IS NOT NULL AND \"PatientReference\" IS NOT NULL)");
                    table.CheckConstraint("ck_invoice_status", "\"Status\" IN ('Draft','Issued','Voided')");
                    table.CheckConstraint("ck_invoice_total", "\"Total\" >= 0");
                    table.CheckConstraint("ck_invoice_void", "(\"Status\" = 'Voided') = (\"VoidedAtUtc\" IS NOT NULL AND \"VoidedByUserId\" IS NOT NULL AND \"VoidReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_patient_invoices_patient_invoices_ReplacesId",
                        column: x => x.ReplacesId,
                        principalTable: "patient_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_invoices_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    Until = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_prices", x => x.Id);
                    table.CheckConstraint("ck_billing_price", "\"Amount\" >= 0 AND (\"Until\" IS NULL OR \"Until\" > \"From\")");
                    table.ForeignKey(
                        name: "FK_billing_prices_billing_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "billing_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_payments", x => x.Id);
                    table.CheckConstraint("ck_payment_amount", "\"Amount\" > 0 AND \"BalanceAfter\" >= 0");
                    table.CheckConstraint("ck_payment_method", "(\"Method\" = 'Cash' AND \"BankReference\" IS NULL) OR (\"Method\" = 'BankTransfer' AND length(trim(\"BankReference\")) > 0 AND \"BankReference\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_patient_payments_patient_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "patient_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_invoice_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LabItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispenseItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Claimed = table.Column<bool>(type: "boolean", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_invoice_items", x => x.Id);
                    table.CheckConstraint("ck_invoice_item_amount", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"Total\" = round(\"Quantity\" * \"UnitPrice\", 2)");
                    table.CheckConstraint("ck_invoice_item_source", "(\"Category\" = 'Consultation' AND \"AppointmentId\" IS NOT NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Laboratory' AND \"LabItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"DispenseItemId\" IS NULL) OR (\"Category\" = 'Pharmacy' AND \"DispenseItemId\" IS NOT NULL AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL) OR (\"Category\" = 'Other' AND \"AppointmentId\" IS NULL AND \"LabItemId\" IS NULL AND \"DispenseItemId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_patient_invoice_items_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_invoice_items_billing_prices_PriceId",
                        column: x => x.PriceId,
                        principalTable: "billing_prices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_invoice_items_laboratory_order_items_LabItemId",
                        column: x => x.LabItemId,
                        principalTable: "laboratory_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_invoice_items_patient_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "patient_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_invoice_items_prescription_dispense_items_DispenseI~",
                        column: x => x.DispenseItemId,
                        principalTable: "prescription_dispense_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_reversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_reversals", x => x.Id);
                    table.CheckConstraint("ck_payment_reversal_reason", "length(trim(\"Reason\")) > 0");
                    table.ForeignKey(
                        name: "FK_payment_reversals_patient_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "patient_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_prices_ServiceId_From",
                table: "billing_prices",
                columns: new[] { "ServiceId", "From" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_services_Code",
                table: "billing_services",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoice_items_InvoiceId",
                table: "patient_invoice_items",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoice_items_PriceId",
                table: "patient_invoice_items",
                column: "PriceId");

            migrationBuilder.CreateIndex(
                name: "ux_billing_appointment_claim",
                table: "patient_invoice_items",
                column: "AppointmentId",
                unique: true,
                filter: "\"Claimed\" AND \"AppointmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_billing_dispense_claim",
                table: "patient_invoice_items",
                column: "DispenseItemId",
                unique: true,
                filter: "\"Claimed\" AND \"DispenseItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_billing_lab_claim",
                table: "patient_invoice_items",
                column: "LabItemId",
                unique: true,
                filter: "\"Claimed\" AND \"LabItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoices_Number",
                table: "patient_invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoices_PatientId_CreatedAtUtc",
                table: "patient_invoices",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoices_ReplacesId",
                table: "patient_invoices",
                column: "ReplacesId",
                unique: true,
                filter: "\"Status\" <> 'Voided'");

            migrationBuilder.CreateIndex(
                name: "IX_patient_invoices_Status_IssuedAtUtc",
                table: "patient_invoices",
                columns: new[] { "Status", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_payments_ActorId_RequestKey",
                table: "patient_payments",
                columns: new[] { "ActorId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_payments_BankReference",
                table: "patient_payments",
                column: "BankReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_payments_InvoiceId",
                table: "patient_payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_payments_ReceiptNumber",
                table: "patient_payments",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_payments_RecordedAtUtc",
                table: "patient_payments",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_reversals_CreatedAtUtc",
                table: "payment_reversals",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_reversals_PaymentId",
                table: "payment_reversals",
                column: "PaymentId",
                unique: true);
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE billing_prices ADD CONSTRAINT ck_billing_price_bounded CHECK ("Until" IS NOT NULL);
                ALTER TABLE billing_prices ADD CONSTRAINT ex_billing_price_period EXCLUDE USING gist
                    ("ServiceId" WITH =, daterange("From", "Until", '[)') WITH &&);

                CREATE FUNCTION billing_prevent_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Financial history is immutable' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER billing_prices_immutable BEFORE UPDATE OR DELETE ON billing_prices
                    FOR EACH ROW EXECUTE FUNCTION billing_prevent_history_change();
                CREATE TRIGGER billing_payments_immutable BEFORE UPDATE OR DELETE ON patient_payments
                    FOR EACH ROW EXECUTE FUNCTION billing_prevent_history_change();
                CREATE TRIGGER billing_reversals_immutable BEFORE UPDATE OR DELETE ON payment_reversals
                    FOR EACH ROW EXECUTE FUNCTION billing_prevent_history_change();
                CREATE TRIGGER billing_invoices_no_delete BEFORE DELETE ON patient_invoices
                    FOR EACH ROW EXECUTE FUNCTION billing_prevent_history_change();

                CREATE FUNCTION billing_guard_invoice() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."Status" <> 'Draft' OR (NEW."ReplacesId" IS NOT NULL AND NOT EXISTS (
                            SELECT 1 FROM patient_invoices old_invoice
                            WHERE old_invoice."Id" = NEW."ReplacesId" AND old_invoice."Status" = 'Voided'
                              AND old_invoice."PatientId" = NEW."PatientId"
                        )) THEN
                            RAISE EXCEPTION 'Invoice must begin as an eligible draft' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF ROW(OLD."PatientId", OLD."ReplacesId", OLD."CreatedAtUtc", OLD."CreatedByUserId") IS DISTINCT FROM
                       ROW(NEW."PatientId", NEW."ReplacesId", NEW."CreatedAtUtc", NEW."CreatedByUserId") OR OLD."Status" = 'Voided'
                    THEN
                        RAISE EXCEPTION 'Invoice identity or terminal state cannot change' USING ERRCODE = '23514';
                    END IF;
                    IF OLD."Status" = 'Draft' AND NEW."Status" NOT IN ('Draft','Issued') THEN
                        RAISE EXCEPTION 'Invalid invoice transition' USING ERRCODE = '23514';
                    END IF;
                    IF OLD."Status" = 'Issued' THEN
                        IF NEW."Status" NOT IN ('Issued','Voided') OR
                           (to_jsonb(OLD) - ARRAY['Status','VoidReason','VoidedByUserId','VoidedAtUtc','Token','UpdatedAtUtc','UpdatedByUserId'])
                           IS DISTINCT FROM
                           (to_jsonb(NEW) - ARRAY['Status','VoidReason','VoidedByUserId','VoidedAtUtc','Token','UpdatedAtUtc','UpdatedByUserId'])
                        THEN
                            RAISE EXCEPTION 'Issued invoice details cannot change' USING ERRCODE = '23514';
                        END IF;
                        IF NEW."Status" = 'Voided' AND (
                            COALESCE(btrim(NEW."VoidReason"), '') = '' OR EXISTS (
                                SELECT 1 FROM patient_payments p LEFT JOIN payment_reversals r ON r."PaymentId" = p."Id"
                                WHERE p."InvoiceId" = NEW."Id" AND r."Id" IS NULL
                            )
                        ) THEN
                            RAISE EXCEPTION 'Void requires a reason and no unreversed payments' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER billing_invoice_guard BEFORE INSERT OR UPDATE ON patient_invoices
                    FOR EACH ROW EXECUTE FUNCTION billing_guard_invoice();

                CREATE FUNCTION billing_guard_line() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE invoice_status varchar;
                BEGIN
                    IF TG_OP = 'UPDATE' AND (to_jsonb(OLD) - 'Claimed') IS DISTINCT FROM (to_jsonb(NEW) - 'Claimed') THEN
                        RAISE EXCEPTION 'Invoice lines can only be replaced while in draft' USING ERRCODE = '23514';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        SELECT "Status" INTO invoice_status FROM patient_invoices WHERE "Id" = OLD."InvoiceId" FOR UPDATE;
                        IF invoice_status <> 'Draft' THEN
                            RAISE EXCEPTION 'Issued invoice lines cannot be deleted' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    SELECT "Status" INTO invoice_status FROM patient_invoices WHERE "Id" = NEW."InvoiceId" FOR UPDATE;
                    IF TG_OP = 'INSERT' AND invoice_status <> 'Draft' THEN
                        RAISE EXCEPTION 'Charges can only be added to draft invoices' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER billing_line_guard BEFORE INSERT OR UPDATE OR DELETE ON patient_invoice_items
                    FOR EACH ROW EXECUTE FUNCTION billing_guard_line();

                CREATE FUNCTION billing_check_invoice_totals() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE CONSTRAINT TRIGGER billing_invoice_totals AFTER INSERT OR UPDATE ON patient_invoices
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION billing_check_invoice_totals();
                CREATE CONSTRAINT TRIGGER billing_item_totals AFTER INSERT OR UPDATE OR DELETE ON patient_invoice_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION billing_check_invoice_totals();

                CREATE FUNCTION billing_guard_payment() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    invoice_total numeric;
                    invoice_status varchar;
                    valid_paid numeric;
                BEGIN
                    SELECT "Total", "Status" INTO invoice_total, invoice_status FROM patient_invoices
                    WHERE "Id" = NEW."InvoiceId" FOR UPDATE;
                    SELECT COALESCE(sum(p."Amount"), 0) INTO valid_paid FROM patient_payments p
                    LEFT JOIN payment_reversals r ON r."PaymentId" = p."Id"
                    WHERE p."InvoiceId" = NEW."InvoiceId" AND r."Id" IS NULL;
                    IF invoice_status IS DISTINCT FROM 'Issued' OR NEW."Amount" > invoice_total - valid_paid OR
                       NEW."BalanceAfter" <> invoice_total - valid_paid - NEW."Amount"
                    THEN
                        RAISE EXCEPTION 'Payment exceeds outstanding invoice balance' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER billing_payment_guard BEFORE INSERT ON patient_payments
                    FOR EACH ROW EXECUTE FUNCTION billing_guard_payment();

                CREATE FUNCTION billing_guard_reversal() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM 1 FROM patient_invoices i JOIN patient_payments p ON p."InvoiceId" = i."Id"
                      WHERE p."Id" = NEW."PaymentId" AND i."Status" = 'Issued' FOR UPDATE OF i;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Payment reversal requires an issued invoice' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER billing_reversal_guard BEFORE INSERT ON payment_reversals
                    FOR EACH ROW EXECUTE FUNCTION billing_guard_reversal();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER billing_reversal_guard ON payment_reversals;
                DROP TRIGGER billing_payment_guard ON patient_payments;
                DROP TRIGGER billing_item_totals ON patient_invoice_items;
                DROP TRIGGER billing_invoice_totals ON patient_invoices;
                DROP TRIGGER billing_line_guard ON patient_invoice_items;
                DROP TRIGGER billing_invoice_guard ON patient_invoices;
                DROP TRIGGER billing_invoices_no_delete ON patient_invoices;
                DROP TRIGGER billing_reversals_immutable ON payment_reversals;
                DROP TRIGGER billing_payments_immutable ON patient_payments;
                DROP TRIGGER billing_prices_immutable ON billing_prices;
                DROP FUNCTION billing_guard_reversal();
                DROP FUNCTION billing_guard_payment();
                DROP FUNCTION billing_check_invoice_totals();
                DROP FUNCTION billing_guard_line();
                DROP FUNCTION billing_guard_invoice();
                DROP FUNCTION billing_prevent_history_change();
                """);

            migrationBuilder.DropTable(
                name: "patient_invoice_items");

            migrationBuilder.DropTable(
                name: "payment_reversals");

            migrationBuilder.DropTable(
                name: "billing_prices");

            migrationBuilder.DropTable(
                name: "patient_payments");

            migrationBuilder.DropTable(
                name: "billing_services");

            migrationBuilder.DropTable(
                name: "patient_invoices");
        }
    }
}
