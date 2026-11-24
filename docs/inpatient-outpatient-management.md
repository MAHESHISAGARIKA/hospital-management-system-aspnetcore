# Inpatient and Outpatient Management

## Outpatient service

OPD Today and Visit History query existing appointments by date, Doctor, Department and status. A linked Doctor sees only their visits; Receptionists handle administrative worklists. Use the appointment detail page for existing check-in and completion actions. Doctors open or visit the existing authorized EMR encounter. A pending admission can be created from that encounter explicitly. It links the patient and attending Doctor without altering the original visit. A second conversion is rejected while the original linked admission remains valid. Walk-in scheduling and cross-provider clinical access are deferred.

## Wards, beds and admission

Administrators configure wards and beds. Receptionists can view capacity, condition, availability and nonclinical allocation history. Occupancy comes from current allocations. A ready, active bed in an active ward is assignable only when empty. Administrators cannot deactivate an occupied ward or edit an occupied bed.

A Receptionist registers a pending admission for an active patient and an active linked Doctor. The linked Doctor can also register a stay from their permitted OPD encounter. The attending Doctor approves the pending admission. Reception can then admit the patient and allocate a ready bed. Pending and approved records can be cancelled with a reason. An admitted or discharged record cannot be cancelled or reopened. Authorization for changing the attending Doctor during a stay is deferred.

Transfers require a reason and a different ready bed. The current allocation closes and the new allocation starts at the same UTC instant. Vacated beds enter Cleaning. An Administrator can explicitly return a bed to Ready after operational review. Maintenance and unavailable conditions block allocation. No automatic release time is assumed.

## Care and discharge

The linked attending Doctor controls care assignments and clinical discharge. An enabled Nurse can work only within an active assignment and admitted stay. Supported observations record numeric temperature in Celsius, pulse and respirations per minute, and oxygen saturation as a percentage. Corrections append a new record with a reason, preserving the original. No interpretations or clinical reference ranges are generated. Receptionists see administrative history with clinical content removed. Administrator and Accountant roles alone provide no clinical access.

The Doctor signs a discharge disposition, diagnosis and treatment/discharge summary. Discharge closes the current allocation and active nursing assignments and puts the bed on Cleaning. The original attending profile retains responsibility for its signed discharge history while active and linked. No clinical writing is available after discharge. Existing linked OPD records still use EMR resource checks. The print view renders the signed discharge and bed history, with separately labeled linked outpatient prescriptions and laboratory references when still authorized. Browser printing and Print to PDF are supported.

## Reporting, Billing and data integrity

Operational reports show census, current ward utilization, available beds, signed discharge activity, transfer history and completed-stay duration in hours. Dates display in Asia/Colombo, with UTC timestamps stored internally. Date filtering on admission worklists uses admission time when available, otherwise registration time. Discharge history uses the signed discharge date. Transfers do not create another admission.

An Accountant may manually charge one discharged admission for its own patient at quantity one on the local discharge date. Configured Admission services use ordinary Billing prices and snapshots. This does not share clinical details or post automatic charges. Room-day tariffs, settlement checks and automatic billing are deferred.

Transactions lock the admission, then affected wards and beds in sorted order. Allocation is rechecked before writes. Unique active-admission and current-bed indexes, PostgreSQL historical exclusion ranges, and deferred consistency checks enforce occupancy. Admission requests are actor-scoped and idempotent. Revision tokens reject stale operations. Operational and clinical changes save with the audit event. Audit descriptions omit reasons, observations and diagnoses.

## Verification

The AddInpatientOutpatientManagement migration requires AddBillingManagement. Generated SQL preserves existing records. Use the [README](../README.md) commands for manual migration of a disposable database. Live PostgreSQL tests require an explicitly configured test database and retain fictional test histories there.

1. As Administrator, register a ward and two beds. Put one on maintenance and check that it is unavailable.
2. As Receptionist, register a fictional patient for a linked Doctor. Verify allocation requires Doctor approval. Open OPD Today and check the same patient's existing visit.
3. As the correct Doctor, approve the admission and have Reception allocate a ready bed. Verify an assigned Nurse can record and correct an observation and an unassigned Nurse is denied.
4. Transfer the patient to a different ready bed, confirm old history and cleaning hold, then sign discharge as Doctor. Print the summary and test other users' access.
5. As Accountant, manually price and charge the discharged admission. Try a mismatched patient and a duplicate charge. Verify a separate clinical action produced no invoice.

Emergency access, walk-ins, direct admission full EMR ordering, staffing schedules and handoffs, formal historical utilization analytics, server-side PDF export and general hospital reporting are deferred. Stage 12 Staff Management owns workforce and staffing policy.
