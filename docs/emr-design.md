# Electronic Medical Records

## Access and visit policy

An authenticated Doctor must have an active doctor profile linked to their enabled Identity account. The service checks the link for every clinical operation and filters encounters by that doctor, the active patient and a matching appointment. An existing encounter can be accessed during a CheckedIn or Completed visit. Opening a new encounter requires CheckedIn. Scheduled, Cancelled and Missed visits grant no clinical access. Appointmentless encounters are reserved for a future approved walk-in workflow. A completed visit can finish documentation in its existing open encounter. Archived patients and inactive doctors are denied by default. The application provides no administrative, nursing, laboratory, pharmacy, accounting or receptionist clinical override. Doctors with an additional role still need the same verified care relationship.

## Record lifecycle

- A clinical encounter connects one patient, one responsible doctor and one checked-in appointment. The encounter finalizes once all drafts are signed. A finalized encounter accepts tracked amendments to its signed entries.
- Diagnoses contain doctor-entered text, an optional code and optional notes. Treatments contain doctor-entered text, a clinical date and optional notes. Prescriptions contain one to twenty items with required medication and administration fields, optional strength, quantity and instructions.
- Draft edits preserve prescription item identifiers while a retained line is edited. Removed draft lines can be deleted. Signed entries and their lines remain intact. A signed correction creates a new revision with its own identifier and mandatory reason. Draft amendments are labeled separately and do not supersede a signed record until finalized. A prescription void is a signed terminal revision with its own reason. The old record remains marked historical.
- Every write checks encounter-level concurrency. Changes to an existing entry also check that entry's version. The shared EF Core context saves each clinical operation and its audit event together.

## Reporting and protection

The patient view lists records in chronological order and supports clinical date and encounter filters. Encounter reports and prescription print views use saved records, show responsible doctor, dates and statuses, and label drafts and historical revisions. Razor encodes clinical content. Clinical responses use no-store cache headers and no artifacts are produced in public folders. The browser print dialog can save PDF. Server-generated PDFs, persisted report editions and patient-facing distribution are deferred. No clinical text is sent to external services. Audit entries use actor, action, UTC timestamp, record identifier and status only. Normal deployment requires protected database storage, restricted database accounts and protected backups.

Clinical dates use Asia/Colombo calendar dates. Created, updated, signed and report generation instants are stored or recorded in UTC and displayed in Asia/Colombo.

## Database and verification

Apply `AddElectronicMedicalRecords` manually to an explicitly selected database. It follows `AddAppointmentBookingFingerprint` and adds three clinical tables, unique indexes, restricted foreign keys and PostgreSQL guards for visit matching, immutable signed entries and deletion prevention. Treat downgrading this migration as a clinical data removal operation. The [README](../README.md) contains the commands and a browser checklist. The default InMemory suite covers the service and MVC workflows. Native PostgreSQL trigger and migration runtime verification requires an explicitly configured disposable database.

Emergency access, nurse assignments, cross-provider history, care expiry or handoff rules, appointmentless encounter eligibility, controlled medicine and prescription validity rules, and prescription dispensing require clinical policy approval or later modules. Stage 7 does not generate diagnoses or recommend medicines.
