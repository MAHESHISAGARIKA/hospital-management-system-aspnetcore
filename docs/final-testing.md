# Final testing

Run these commands from the repository root with the .NET 10 SDK.

```powershell
Remove-Item Env:HOSPITAL_TEST_POSTGRES_OPT_IN -ErrorAction SilentlyContinue
dotnet restore
dotnet build HospitalManagementSystem.slnx
dotnet test HospitalManagementSystem.slnx
dotnet build HospitalManagementSystem.slnx -c Release
dotnet test HospitalManagementSystem.slnx -c Release
```

The MVC fixture uses EF Core InMemory and fictional records. The seven PostgreSQL cases cover appointments, EMR, laboratory, pharmacy, billing, inpatient and reporting. They skip by default. Staff overlap and leave concurrency still need additional PostgreSQL coverage.

## Isolated PostgreSQL

Use a separate local PostgreSQL 18 instance on a dedicated loopback port with password authentication. Confirm the port belongs to that instance. Start with an empty cluster independent of development and production. In the commands below replace the role and port placeholders for the provisioning session. Keep the test role name and database name as shown because the guard checks them.

```powershell
psql -X -h 127.0.0.1 -p <isolated-port> -U <cluster-admin> -d postgres
```

Run these statements in that explicit provisioning session. Use the interactive password prompt.

```sql
CREATE ROLE hms_integration_test LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
\password hms_integration_test
CREATE DATABASE hospital_management_integration_test;
COMMENT ON DATABASE hospital_management_integration_test IS 'HMS disposable integration test database';
REVOKE ALL ON DATABASE hospital_management_integration_test FROM PUBLIC;
GRANT CONNECT ON DATABASE hospital_management_integration_test TO hms_integration_test;
```

Generate and review the migration SQL using the offline procedure in [Deployment guide](deployment-guide.md). Intentionally apply it only to the isolated target as the database owner. Check the server port and use psql's `\conninfo` before executing the file. After migration, grant the test account limited access inside the isolated database.

```sql
GRANT USAGE ON SCHEMA public TO hms_integration_test;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hms_integration_test;
REVOKE INSERT, UPDATE, DELETE ON TABLE public."__EFMigrationsHistory" FROM hms_integration_test;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hms_integration_test;
```

Supply `HOSPITAL_APPOINTMENT_TEST_CONNECTION` through your secret store or a process-scoped secret prompt. Its Host is the literal `127.0.0.1` or `::1`, Port is the isolated port, Database is `hospital_management_integration_test`, and Username is `hms_integration_test`. Leave sensitive error details, parameter logging and persistence of security information off.

```powershell
$env:HOSPITAL_TEST_POSTGRES_OPT_IN = '1'
try {
    dotnet test HospitalManagementSystem.slnx -c Release
} finally {
    Remove-Item Env:HOSPITAL_TEST_POSTGRES_OPT_IN
    Remove-Item Env:HOSPITAL_APPOINTMENT_TEST_CONNECTION -ErrorAction SilentlyContinue
}
```

The guard checks server identity and role privilege flags before test writes. Missing or invalid configuration after opt-in fails the test. Some legacy cases commit records to exercise concurrency. Provision a fresh database for each run. Clean up by stopping and retiring the entire isolated cluster under the test operator's control after taking any fictional recovery archive needed for the demonstration.

## Acceptance checklist

Record the build identifier, environment, role, expected result, observed result and pass or fail for each item. Use fictional data.

| Workflow | Browser acceptance |
| --- | --- |
| Consultation | Register a patient, book with an eligible doctor, check in, finalize the encounter and verify another doctor cannot open it. |
| Laboratory | Request a test from that encounter, collect and verify a result, then check the ordering doctor's permitted view and an unrelated user's denial. |
| Pharmacy | Receive fictional stock, dispense an eligible partial and final quantity against a finalized fictional prescription, then verify ledger, over-dispense rejection and retry. |
| Billing | Issue an invoice, make partial and final payments, view receipts, reject overpayment and reverse a payment with history. |
| Inpatient | Admit, allocate a bed, transfer, discharge and verify care assignment restrictions and bed release. |
| Staff | Link an account, assign department, clock in and out, correct a closed session and verify approver separation and clinical conflicts. |
| Reports | Compare authorized dashboards with report values and CSV, check Colombo midnight boundaries, and preview a printed report. |
| Usability | Check phone-width menus, sign-in and sign-out, validation, empty states, denied links, invalid IDs and database-unavailable behavior. |

The existing test suites exercise these workflows by module. The browser checklist also checks how the modules work together and needs a separate execution record.

## Current verification scope

The earlier opt-in attempt was reported to fail the connection safety guard. The rejected field was not established in this review. Leave the guard in place and use the normal suite for the demonstration. The [supervisor guide](supervisor-demo.md) records the manual checks to run against an already prepared fictional-only database.
