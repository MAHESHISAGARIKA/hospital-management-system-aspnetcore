# Supervisor demonstration

## Before the meeting

Use an already migrated, dedicated local demonstration database containing fictional records only. Confirm the target with psql `\conninfo` and confirm the app's effective connection targets the same database. Keep patient document storage separate from the development store. Leave PostgreSQL test opt-in disabled. Start the app using the [README setup](../README.md#getting-started). Check that `/health/live` and `/health/ready` both return 200 before demonstrating data workflows.

If the isolated target is unavailable, present the recorded build and test results and walk through this guide. Record live workflow coverage as NOT VERIFIED.

### Prepare accounts

On the isolated demo database, use an existing demo Administrator or run the Development-only first-administrator bootstrap in the README. Sign in and use **Manage users ? Create** to create one account per role with an `example.test` address, such as `demo-doctor@example.test`. Enter a unique temporary password from a private password manager and complete the mandatory first-sign-in password change before screen sharing. Keep accounts in separate browser profiles or sign out between roles. Keep passwords, secret configuration, and the browser password manager off screen.

| Role | Show | Permission boundary to check |
| --- | --- | --- |
| Administrator | User accounts, departments, employee registration, and operational reports | Billing requires Accountant. Clinical records require clinical authorization. |
| Receptionist | Patient registration, appointment booking and check-in, admission worklist and bed allocation | Billing and clinical narratives are outside the role. |
| Doctor | Linked appointment, encounter, lab request and finalized prescription | A second doctor's unrelated encounter is denied. |
| Nurse | Assigned inpatient case and permitted observations | An unassigned case is denied. |
| LaboratoryStaff | Sample collection, result entry and the permitted verification workflow | General EMR and Billing are outside the role. |
| Pharmacist | Medicine batches, eligible stock and dispensing against a finalized prescription | General EMR and Billing are outside the role. |
| Accountant | Invoice, payment, receipt and Billing report | Clinical narratives, pharmacy dispensing and staff administration are outside the role. |

### Prepare fictional data in dependency order

1. As Administrator, create a demo department, a doctor profile linked to the Doctor account, and a weekly schedule with a free slot. Create an employee profile linked to the Nurse account and assign its department. Create a leave type and prepare a pending request as the linked employee if showing leave approval.
2. As Administrator, prepare an active demo laboratory test. As Pharmacist, prepare a fictional catalog medicine and receive an eligible batch with matching units and a future expiry date.
3. As Receptionist, register two fictional patients with names prefixed `DEMO`. Book one into the demo doctor's schedule and check it in. If showing a second provider restriction, create a separate linked doctor account and unrelated encounter in advance.
4. As Doctor, open the linked encounter and use clearly fictional clinical entries. Request an active demo lab test and finalize a fictional prescription with a catalog medicine and dispensing unit. Have LaboratoryStaff prepare one collected sample and one verified result in advance if time is short.
5. As Pharmacist, leave one eligible finalized prescription ready to dispense and prepare a completed example in the ledger.
6. As Accountant, configure an effective fictional consultation price. Prepare an issued invoice with a partial payment and an outstanding balance. Keep a second invoice ready if demonstrating payment or reversal live.
7. As Administrator, create an active demo ward with two ready beds. Register and approve an admission using the Receptionist and Doctor accounts. Allocate a bed as Receptionist and assign the Nurse as Doctor. Keep the second bed free if demonstrating transfer.

### Twelve-minute walkthrough

| Time | Show the supervisor |
| --- | --- |
| 0?2 min | Open Dashboard as Administrator. Show operational cards, staff account linking and the role boundary for financial reports. |
| 2?5 min | Switch to Receptionist to show the fictional patient and check-in. Switch to Doctor to show the linked encounter, lab request and finalized prescription. |
| 5?8 min | Show the lab sample and verification status as LaboratoryStaff. Show eligible stock, dispensing and ledger history as Pharmacist. |
| 8?10 min | Show the issued invoice, partial payment, receipt and LKR balance as Accountant. Show the assigned inpatient worklist as Nurse. |
| 10?12 min | Show report filters and print or summary CSV. Demonstrate one denied direct route and explain which release gates remain open. |

### Manual browser checklist

All items start **NOT VERIFIED**. Record the date, build, isolated environment, role, observed outcome and evidence after each check.

- Sign in and out under all seven roles. Check permitted navigation and the denied cases in the role table.
- Register, book, check in and open the linked encounter. Check slot conflict handling and unrelated provider access.
- Verify a lab sample and result, dispense fictional stock, and verify the movement ledger.
- Confirm the invoice balance, receipt and reversal history using fictional transactions.
- Transfer and discharge an inpatient, verify bed release, and check assigned Nurse access.
- Link staff, clock in and out, and verify pending leave and approval history.
- Check report date filters, pagination, print preview, CSV, and direct denial of restricted export routes.
- Check phone-width navigation, empty states, validation messages and sign-out on a shared browser.
