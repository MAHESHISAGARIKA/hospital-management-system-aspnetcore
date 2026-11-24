# Laboratory Management

## Permissions and workflow

- Administrators manage the test catalog. Codes are unique even for inactive tests. An order item keeps its original code, name, specimen type, reporting unit and result type when the catalog changes.
- An active linked Doctor can request 1 to 20 distinct active tests for an open, eligible encounter. Patient and doctor come from the encounter. Repeating the same request key and payload returns the original order. Ordinary cancellation requires a reason and is limited to requests without collection history.
- Enabled LaboratoryStaff use the hospital-wide lab queue. This project has a single laboratory and no staff assignment model. The role permits sample collection, result entry, verification and reports, without direct access to diagnoses, prescriptions or broad EMR history. Other roles have no implicit clinical access. Catalog administration confers no report permission.
- Staff confirm the patient and order numbers to collect a sample. Each sample belongs to one test item. It progresses from Collected to Received to Processing. Before result entry, rejection requires a reason. A replacement gets a new accession, preserving rejected samples.
- Results are Draft, Submitted, then Verified. Staff can return a submission to Draft for correction before verification. The verifier and entry, submission and verification timestamps are recorded. The LaboratoryStaff role can verify its own submitted results. Independent second-person verification and nurse collection require policy approval.
- An order completes only once every item has a verified result. Doctors can follow status earlier and see results after release. A correction makes a new draft linked to the latest verified revision and requires a reason. The prior signed result remains current until the correction is verified.

## Data, reports and auditing

Stage 8 adds tables for the catalog, orders, order items, samples and results. Patient and Doctor are derived from the encounter. Full GUID-derived identifiers and database unique indexes prevent duplicate order numbers and sample accessions. A per-encounter key protects against double submission. Composite foreign keys prevent cross-item sample and revision references. A partial unique index permits only one current specimen per item. Quantitative values use PostgreSQL numeric(18,6). Audit entries and changes save together. General audits omit result values, clinical notes, and correction or rejection reasons.

Reports use saved, verified values. The printable report shows patient and order identifiers, requesting doctor, specimen, verifier, timestamp, result and unit. The order history preserves prior verified results. Clinical responses use no-store cache headers and Razor encodes text. Stored timestamps use UTC. Display uses Asia/Colombo.

## Manual verification

Use a disposable PostgreSQL database and fictional identities. Run the migration and opt-in test commands in the [README](../README.md). The immediate migration dependency is AddElectronicMedicalRecords. Live PostgreSQL checks require a separately configured test database.

1. Sign in as an Administrator and register two fictional tests, one quantitative and one qualitative. Test duplicate codes and deactivate a catalog entry.
2. Sign in as a linked Doctor, check in an appointment, open the encounter and order two active tests. Verify the encounter and timeline links.
3. Sign in as LaboratoryStaff. Confirm collection fails with a mismatched patient or order number. Collect, reject and recollect a sample, then receive and process both specimens.
4. Enter, edit, submit and verify results. Confirm that a report is unavailable until all results are verified and that a Doctor sees no draft content. Print the released report or save it as PDF.
5. Create a correction and verify that the previous result remains current until the correction is verified. Confirm that the earlier signed result and rejected specimen remain in history.
6. Try a stale form and open the report as an unrelated Doctor, Administrator or Receptionist. Confirm the protected operations fail.

Approved reference versions, critical-result notifications, independent verification, multi-site assignments, persisted report editions and server-side PDF export are deferred. No reference ranges are seeded. Future Billing can use order identifiers and separately approved charge metadata. Stage 8 creates no billing transactions.

Stage 10 can manually bill eligible source lines under configured prices. See [Billing and Payments](billing-management.md).
