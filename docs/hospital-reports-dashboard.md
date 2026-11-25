# Hospital reports and dashboard

Open **Dashboard** for role-scoped summary cards and **Reports** for individual reports. Each report uses the same query and access checks for its browser, print and CSV routes. The application uses the existing domain tables through EF Core projections. It aggregates in the database and pages detail tables in batches of 25. Bounded catalog lists contain up to 250 choices. Groupings labelled with a limit show the leading categories.

## Report sources

| Report | Source and date definition |
| --- | --- |
| Patients | Patient records are counted once by ID. Registration trends and the registration list use `Patient.CreatedAtUtc`. Activity counts use distinct patients with a completed appointment by `Appointment.StartAtUtc`. Department matching uses appointments in the period. Current active or archived status comes from the patient record. |
| Appointments | Status and grouping counts use `Appointment.StartAtUtc`. Booking activity uses `Appointment.CreatedAtUtc`. A reschedule updates the original row. Today's count uses the current Colombo day. |
| Billing | Invoice and service-line totals use current issued invoices and `IssuedAtUtc`. Cash uses `PatientPayment.RecordedAtUtc` and excludes payments with reversals. The reversal report uses `PaymentReversal.CreatedAtUtc`. Outstanding uses all unreversed payments for invoices issued during the selected period. Category filtering selects matching invoices for cash and balance figures and matching lines for category totals. All amounts use decimal LKR. |
| Pharmacy | Current stock uses remaining `MedicineBatch.Available`. Eligible stock also requires an active medicine, a compatible unit, available status and an expiry date on or after today. Ledger and dispensing events use `OccurredAtUtc`. Partial dispensing comes from the stored receipt status. |
| Laboratory | The order cohort uses `LaboratoryOrder.CreatedAtUtc`. Order counts are independent of item count. Test breakdowns count matching orders. Samples and result versions belong to that cohort. Additional samples are a count beyond the first per item. Mean turnaround runs from sample creation to recorded verification on an accepted sample. Department uses the requesting provider's current assignment. |
| Staff | The current directory and department counts use employee profiles and open assignment periods. Worked hours clip completed attendance intervals to the selected period before summing. Open sessions are shown as current and excluded from worked hours. Leave uses inclusive local date overlap. Assignment history uses UTC interval overlap. |
| Inpatients | Current census uses admitted records. Period counts use first admission or signed discharge timestamps. Open bed allocations determine occupancy. An active, ready, unallocated bed in an active ward is available. Transfers do not create a new admission. |
| Outpatients | The Stage 11 worklist is appointment-backed. Period and status summaries use appointment occurrence and include the existing scheduled and terminal states. |

Filters accept Asia/Colombo calendar days, inclusive at both ends in the user interface. Queries convert the first day and the day after the last into a half-open UTC window. The default covers today and the previous 29 days. A range can span at most 366 days. Point-in-time metrics use the current time even when a historical period is selected.

## Access and export

- Administrators get registration, appointment, inpatient, pharmacy inventory, laboratory workload and staff summaries.
- Receptionists get patient registration, appointment and inpatient and outpatient operational reports.
- Accountants get the financial report.
- Pharmacists get inventory, stock ledger and dispensing summaries. Laboratory Staff get operational order, sample and result-status counts.
- Doctors get linked appointment and inpatient summaries. Nurses get currently assigned inpatient summaries.

Report aggregates omit diagnoses, clinical result values, prescriptions, private leave reasons and payment secrets. Patient registration details stay in the protected report view. CSV exports include aggregate sections, use UTF-8 and escape text cells for spreadsheets. Browser Print can save the dedicated print view as PDF. The audit records report category and actor for exports, restricted patient report views and denied access. Ordinary dashboard reads do not generate report audit records.

## Verification and deployment

Run `dotnet restore`, `dotnet build HospitalManagementSystem.slnx`, and `dotnet test HospitalManagementSystem.slnx`. The optional Npgsql reporting case uses an explicitly disposable, already migrated test database and rolls back the fictional account. Prepare the database outside the application through your test-environment workflow.

For browser acceptance, sign in under each role and compare the dashboard links with its allowed reports. Check a single day around Colombo midnight, an empty interval, page links with filters, and an invalid range. Check print preview and compare exported aggregate values with the HTML values. Confirm another doctor's filter returns no unrelated appointments and a disallowed direct CSV route reaches Access Denied.

No Stage 13 schema migration required. Deploy against the existing Stage 12 schema. Query timing at production data volumes and live PostgreSQL execution need separate verification. Grouped queries use existing indexes and bounded output. Dashboard sections execute sequentially against one scoped context. Review query plans and response times before adding reporting-specific indexes. Reports reflect current data and status, so they do not reconstruct past inventory, invoices as of a historical cutoff, or historic staffing levels. No shift-based absence, overtime, recognized revenue or statutory leave figures are inferred. Stage 14 deployment should include a staging PostgreSQL run and a role-by-role browser acceptance check.
