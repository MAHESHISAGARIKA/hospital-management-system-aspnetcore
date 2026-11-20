# Requirements Matrix

This document maps the proposed Hospital Management System requirements to future modules, acceptance criteria, and tests. It is a planning document, not evidence that a feature is implemented.

## Current implementation status

- **Implemented:** ASP.NET Core MVC shell, existing homepage, PostgreSQL connection configuration through User Secrets, EF Core/Npgsql registration, initial `Patient` and `Department` entities, ASP.NET Core Identity, seven hospital roles, login/logout, password change, Administrator user management, role authorization and security audit logging.
- **Not implemented:** Patient workflows, department workflows and the clinical, appointment, laboratory, pharmacy, billing, admission, staffing and reporting modules listed below.

| ID | Requirement | Proposed module | Acceptance criteria | Future tests |
|---|---|---|---|---|
| SEC-01 | Administrator, Doctor, Nurse, Receptionist, Laboratory Staff, Pharmacist, and Accountant roles | Identity and Authorization | A user can be assigned only to approved roles; each module denies or allows actions according to role policy | Role assignment tests, unauthorized access tests, policy unit/integration tests |
| SEC-02 | Authentication, logout, password management | Identity and Authorization | Login/logout work; passwords are validated and stored securely; password reset/change follows policy | Valid/invalid login tests, lockout tests, password reset tests, logout session tests |
| SEC-03 | Secure sessions and concurrency control | Identity, Shared Infrastructure | Sessions expire according to policy; stale record edits are detected and rejected | Session timeout tests, concurrency conflict tests |
| SEC-04 | Audit logging | Audit and Compliance | Security and clinical/business changes record actor, timestamp, action and relevant entity without secrets | Audit writer tests, audit immutability tests, correlation tests |
| PAT-01 | Patient registration and updates | Patient Management | A patient can be created or updated with validated required and optional fields | Validation tests, create/update tests, unauthorized update tests |
| PAT-02 | Patient search | Patient Management | Search works by approved identifiers and name fields without exposing unrelated records | Search boundary tests, duplicate result tests, authorization tests |
| PAT-03 | Medical history | Patient Management / EMR | Authorized staff can view chronological patient history and linked clinical records | History ordering tests, access tests, no-record tests |
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
| BIL-01 | Billing charges | Billing | Consultation, laboratory, pharmacy and admission charges produce auditable invoice lines | Charge tests, total tests, authorization tests |
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
