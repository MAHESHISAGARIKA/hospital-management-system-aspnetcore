# Requirements Matrix

This document maps the proposed Hospital Management System requirements to future modules, acceptance criteria, and tests. It is a planning document, not evidence that a feature is implemented.

## Current implementation status

- **Implemented:** ASP.NET Core MVC shell, homepage, PostgreSQL/User Secrets configuration, EF Core/Npgsql, Identity and seven roles, login/logout, password change, administrator management, audit logging, and Stage 4 patient directory, registration, profile, editing, archival/reactivation, duplicate warnings, private document metadata/download routes and the administrative patient profile.
- **Stage 5 implemented:** Doctor and DoctorSchedule entities, department assignment, doctor directory, registration/edit/active status, schedule CRUD/deactivation, account-link services and least-privilege MVC routes. The Asia/Colombo timezone is documented as schedule display/entry context.
- **Schedule limitation:** Overnight sessions are rejected. Holidays, leave and one-off unavailability are not modeled.
- **Stage 6 implemented:** Appointment booking, directory search/filtering, day/week/month range calendar, details, rescheduling, cancellation, lifecycle status changes, actor-scoped booking retries, audit events and UTC storage with Asia/Colombo presentation.
- **Stage 6 verification limitation:** EF InMemory tests cover service rules. MVC tests cover appointment permissions, doctor options and directory paging. PostgreSQL GiST exclusion, concurrent retries, reschedule locking and rollback require the opt-in integration suite on a disposable PostgreSQL test database.
- **Stage 7 implemented:** Doctor-scoped encounters for checked-in visits, diagnosis and treatment drafts, multi-item prescriptions, signed revisions and prescription voids, patient timelines, and browser-printable clinical reports. PostgreSQL trigger verification requires the opt-in disposable-database suite.
- **Stage 8 implemented:** Laboratory catalog, doctor-scoped requests, specimen tracking, staff result verification, corrections and browser-printable reports. Native PostgreSQL verification requires a disposable database.
- **Stage 9 implemented:** Catalog, batch stock, ledger, expiry and low-stock monitoring, and partial dispensing against signed EMR prescriptions. PostgreSQL verification is opt-in on a disposable database.
- **Stage 10 implemented:** LKR price catalog, clinical-source invoices, payment recording and reversals, receipt printing, outstanding balances and billing reports. PostgreSQL verification is opt-in.
- **Stage 11 implemented:** OPD worklists and summaries on existing appointments, ward and bed management, approved stays and transfers, scoped observations, Doctor-signed discharge and manual Admission billing. PostgreSQL verification remains opt-in.
- **Not implemented:** staffing and reporting modules.

| ID | Requirement | Proposed module | Acceptance criteria | Future tests |
|---|---|---|---|---|
| SEC-01 | Administrator, Doctor, Nurse, Receptionist, Laboratory Staff, Pharmacist, and Accountant roles | Identity and Authorization | A user can be assigned only to approved roles; each module denies or allows actions according to role policy | Role assignment tests, unauthorized access tests, policy unit/integration tests |
| SEC-02 | Authentication, logout, password management | Identity and Authorization | Login/logout work; passwords are validated and stored securely; password reset/change follows policy | Valid/invalid login tests, lockout tests, password reset tests, logout session tests |
| SEC-03 | Secure sessions and concurrency control | Identity, Shared Infrastructure | Sessions expire according to policy; stale record edits are detected and rejected | Session timeout tests, concurrency conflict tests |
| SEC-04 | Audit logging | Audit and Compliance | Security and clinical/business changes record actor, timestamp, action and relevant entity without secrets | Audit writer tests, audit immutability tests, correlation tests |
| PAT-01 | Patient registration and updates | Patient Management | A patient can be created or updated with validated required and optional fields | Validation tests, create/update tests, unauthorized update tests |
| PAT-02 | Patient search | Patient Management | Search works by approved identifiers and name fields without exposing unrelated records | Search boundary tests, duplicate result tests, authorization tests |
| PAT-03 | Medical history | Patient Management / EMR | An active linked doctor views only the timeline for their permitted encounters | History ordering tests, access tests, no-record tests |
| PAT-04 | Document uploads | Document Management | Accepted documents can be uploaded, linked to a patient, virus scanned if available, and permission checked | File type/size tests, ownership tests, download authorization tests |
| DOC-01 | Doctor profiles | Doctor Management | Doctor profile stores identity, department, active status and approved qualifications | Create/update tests, deactivation tests, department-link tests |
| DOC-02 | Departments | Department Management | Department codes/names are unique and a department can be activated/deactivated | Unique constraint tests, update/deactivation tests |
| DOC-03 | Doctor schedules | Scheduling | Schedules prevent invalid times and make availability visible by policy | Overlap tests, timezone tests, availability tests |
| APT-01 | Appointment booking | Appointments | Authorized users can book an appointment within valid doctor availability | Booking tests, availability conflict tests, patient/doctor validation tests |
| APT-02 | Cancellation and rescheduling | Appointments | Approved status transitions update appointment state and audit trail | Cancellation tests, reschedule tests, invalid transition tests |
| APT-03 | Appointment status tracking | Appointments | Status progresses through explicitly approved states and is visible to authorized roles | Status transition tests, filtering tests |
| EMR-01 | Diagnoses and treatments | Electronic Medical Records | Clinical entries reference the patient and authorized provider and preserve history | Record integrity tests, provider authorization tests |
| EMR-02 | Prescriptions | Prescription Management | Prescriptions contain medication, dose, route, duration and prescriber | Required-field tests, dispensing relationship tests, authorization tests |
| EMR-03 | Medical reports | Clinical Reporting | Approved reports are linked to patient/encounter and controlled by status | Report lifecycle tests, access tests, immutability tests |
| LAB-01 | Laboratory requests | Laboratory | An authorized provider can request approved tests for a patient | Request validation tests, status tests |
| LAB-02 | Sample collection | Laboratory | Collected samples record collector, time, status and chain of custody | Sample tests, invalid transition tests, audit tests |
| LAB-03 | Laboratory results and reports | Laboratory | Results are linked to requests, verified, and reported to authorized viewers | Verification tests, abnormal-value tests, report access tests |
| PHA-01 | Pharmacy inventory and stock control | Pharmacy | Stock changes are tracked and cannot become negative without approval/policy | Stock movement tests, negative stock tests, expiry tests |
| PHA-02 | Prescription processing | Pharmacy | Dispensing reduces stock and records the prescription, quantity and dispenser | Dispensing tests, duplicate dispense tests, stock integrity tests |
| PHA-03 | Expiry monitoring | Pharmacy | Expiring and expired batches are reported and unavailable for normal dispensing | Expiry boundary tests, batch selection tests |
| BIL-01 | Billing charges | Billing | Eligible consultation, laboratory and pharmacy sources produce auditable invoice lines. A discharged admission can support one explicitly priced manual charge. | Charge tests, total tests, authorization tests |
| BIL-02 | Invoices and payments | Billing | Invoice totals and payment allocations reconcile and cannot be silently altered | Invoice tests, overpayment/refund tests, payment tests |
| BIL-03 | Receipts | Billing | A successful payment produces a traceable receipt | Receipt tests, duplicate payment tests |
| ADM-01 | Inpatient/outpatient management | Admissions | Each encounter has the correct patient, type, status and accountable provider | Admission/discharge tests, invalid transition tests |
| STF-01 | Staff registration | Staff Management | Staff records are validated and linked to one or more approved departments | Staff validation tests, department-link tests |
| STF-02 | Attendance and leave | Staff Management | Attendance and leave are recorded with approvals and audit history | Attendance tests, leave overlap tests, approval tests |
| REP-01 | Patient reports | Reporting | Authorized users receive accurate patient activity summaries | Data accuracy tests, date-range tests, role tests |
| REP-02 | Appointment/revenue reports | Reporting | Reports reconcile to appointment and billing source data | Reconciliation tests, aggregate tests, export tests |
| REP-03 | Pharmacy/laboratory/staff reports | Reporting | Reports use approved source records and respect access controls | Role tests, stock/lab/staff reconciliation tests |
| OPS-01 | Dashboard | Dashboard | Authorized dashboard cards reflect current source data and do not expose restricted details | Widget tests, cache freshness tests, role tests |
| OPS-02 | Responsive interface | Shared UI | Core workflows remain usable on supported desktop and mobile viewport sizes | Responsive UI tests, accessibility tests |
| OPS-03 | Database backup and recovery | Operations | Backups are scheduled, restorable, encrypted and recovery objectives are approved | Backup tests, restore drill tests, recovery/RPO/RPO checks |
| OPS-04 | Concurrency and data integrity | Shared Infrastructure | Updates use transactions and concurrency controls where required | Isolation tests, rollback tests, stale update tests |

## Future test strategy

Future tests should include unit tests for domain rules, EF Core integration tests against disposable PostgreSQL test instances, MVC/integration tests for authorization and HTTP behavior, and end-to-end tests for critical role workflows.


## Stage 12 Staff Management

| Requirement | Implementation |
| --- | --- |
| Employee administration and department history | Employee directory, existing Identity account linking, activation and dated department assignments |
| Attendance and corrections | Staff self-service clock actions and administrator corrections with prior values retained |
| Leave and clinical safeguards | Leave types and request history, administrator decisions and checks against known clinical commitments |
| Staff reporting | Dashboard totals and staff, attendance and leave views |
| Payroll, leave entitlements and schedules | Deferred until the hospital defines the applicable policies |

## Stage 13 Hospital reports and dashboard

Patient, appointment, billing, pharmacy, laboratory and staff reports are available with role-scoped dashboard sections. Inpatient and outpatient reports use Stage 11 records. The [reporting guide](hospital-reports-dashboard.md) documents query definitions, safeguards and verification limits.

## Stage 14 acceptance evidence

Verification below refers to automated coverage unless a manual run is listed. The requirements checklist and prior module sections retain the original scope.

| Requirement | Status | Evidence and remaining manual check |
| --- | --- | --- |
| Identity and role management | Implemented and verified | `AccountController`, `UsersController`, `AuthenticationTests`; confirm production cookie and password provisioning. |
| Patient profile, archive and documents | Implemented and verified | `PatientsController`, `PatientManagementTests`; confirm deployed file storage permissions. |
| Doctor and schedule management | Implemented and verified | `DoctorsController`, `DoctorManagementTests`; confirm schedules with a linked account in staging. |
| Appointment booking and lifecycle | Implemented and verified | `AppointmentsController`, `AppointmentManagementTests`; concurrent PostgreSQL protection is implemented but unverified. |
| EMR scope and amendment history | Implemented and verified | `ClinicalRecordService`, `ClinicalRecordTests`; PostgreSQL finalization trigger remains unverified. |
| Laboratory order, sample and result workflow | Implemented and verified | `LaboratoryController`, `LaboratoryTests`; PostgreSQL history enforcement remains unverified. |
| Pharmacy inventory, dispensing and ledger | Implemented and verified | `PharmacyService`, `PharmacyTests`; PostgreSQL concurrent stock enforcement remains unverified. |
| Billing, payments, reversals and balances | Implemented and verified | `BillingController`, `BillingTests`; PostgreSQL races and precision need the isolated run. |
| Inpatient and outpatient workflows | Implemented and verified | `InpatientsController`, `OutpatientsController`, `InpatientTests`; PostgreSQL allocation and transfer tests remain unverified. |
| Staff profiles, attendance and leave | Implemented and verified | `StaffController`, `AuthenticatedStaffTests`; cross-module approval races and live staff constraints remain unverified. |
| Six reports and role dashboard | Implemented and verified | `ReportsController`, `HospitalReportingTests`; PostgreSQL aggregation and manual print preview remain unverified. |
| Session timeout and request security | Implemented and verified | `Program.cs`, `AuthenticationTests`, `AuthenticatedStaffTests`; verify HTTPS and live session expiry. |
| Audit logging | Implemented and verified | `AuditLogService`, report and staff test coverage; confirm deployed retention and permissions. |
| Responsive interface | Implemented but unverified | Bootstrap shared layout; exercise narrow screens and print in the acceptance checklist. |
| Backup and restore | Partially implemented | Operator procedures exist. The restore drill and recovery timing are unverified. |
| Performance testing | Implemented but unverified | Pagination and query review exist. No production-scale benchmark has been run. |
| Production deployment | Deferred | A target host, secure provisioning, a verified backup, and a staging deployment are required. |

See the [release gates](release-readiness.md) for current acceptance status.
