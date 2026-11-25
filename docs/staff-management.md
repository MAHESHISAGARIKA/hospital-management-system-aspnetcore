

# Staff management

Administrators open Staff Management to create employee profiles, link existing enabled Identity accounts, assign departments and maintain leave types. Staff can use My staff profile with an active employee profile linked to their enabled account. Link an employee to the same Identity account used by their clinical profile.

Department transfers close the previous assignment and retain its history. Deactivate employees after resolving open attendance and clinical work. Review clinical schedules and care assignments through their existing workflows.

Staff clock in and out through My staff profile. Times are stored in UTC and shown in Asia/Colombo. Correct a completed session from the administrator attendance view. The correction records the previous and corrected times, reason and actor.

Staff can submit, edit and withdraw pending leave. Dates are inclusive calendar dates in Asia/Colombo. Administrators review pending requests and can approve or reject another employee's request. Approval checks scheduled or checked-in appointments and active inpatient responsibility linked to the employee's account. Resolve conflicts through the clinical modules and then retry approval. Department rosters, future inpatient responsibility and cross-module booking races require additional integration.

The dashboard shows employee, department, attendance and pending leave totals. Reports show filtered records in the browser. The current view pages employees and limits attendance and leave rows per page. Review the employee detail page for an individual record.

Audit entries identify the operation, actor and affected staff record. Reasons are stored in the corresponding restricted workflow records. Administrators should review authorized access before sharing or printing staff records.

## Deployment and checking

Review the AddStaffManagement migration and arrange a database backup through the normal deployment process. An authorized operator can apply it to the configured environment with the following command.

```powershell
dotnet ef database update AddStaffManagement --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext
```

Use a disposable test environment. Check employee creation, duplicate Identity links, department transfer history and deactivation as an administrator. Sign in as a linked employee and verify access to only their own record. Clock in and out, correct a session and verify its history. Submit leave, reject and withdraw pending requests and check that clinical commitments prevent approval. Verify dashboard figures and report filters against the test records.

Run dotnet test HospitalManagementSystem.slnx for the automated suite. Verify PostgreSQL exclusions using concurrent transactions against a disposable PostgreSQL database. PostgreSQL execution and interactive browser checks are separate from the automated in-memory MVC tests.

Payroll, statutory balances, holiday calendars, shift calculations, biometric and GPS attendance remain deferred. Stage 13 can consume the staff records and audited outcomes once reporting access and export policies are defined.
