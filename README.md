# Hospital Management System

A web-based Hospital Management System built with C# and ASP.NET Core MVC.

## Overview

This project aims to provide a centralized platform for managing hospital operations, including patient registration, doctor management, appointment scheduling, electronic medical records, laboratory services, pharmacy inventory, billing, and staff administration.

## Technology Stack

- C#
- ASP.NET Core MVC (.NET 10)
- Razor Views
- Bootstrap, HTML, CSS, JavaScript
- PostgreSQL (planned)
- Entity Framework Core (planned)
- ASP.NET Core Identity (planned)

## Planned Features

- Role-based user management
- Patient and doctor management
- Appointment scheduling
- Electronic medical records
- Laboratory management
- Pharmacy management
- Inpatient and outpatient management
- Billing and payments
- Staff management
- Reports and analytics
- Audit logging and database backups

## Project Structure

- `src/HospitalManagementSystem.Web` — Main web application
- `docs` — Project documentation

## Getting Started

### Prerequisites

- .NET 10 SDK
- Git

### Run Locally

```bash
dotnet restore
dotnet build
dotnet run --project src/HospitalManagementSystem.Web
```

Open the URL displayed in the terminal.

## Development Status

- Stage 1: MVC foundation completed.
- Stage 2: Initial `Patient` and `Department` EF foundation completed.
- Stage 3: Identity, login/logout, password change, role authorization, administrator user management and audit logging implemented.
- Stage 4: Patient directory, registration, profile, editing, archive/reactivation, duplicate warnings and secured document metadata/download routes implemented. Complete EMR workflows remain pending.
- Stage 5: Doctor profiles, department assignment, weekly schedules, account linking and least-privilege doctor routes implemented.
- Stage 6: Appointment booking, directory, details, rescheduling, cancellation, status lifecycle and day/week/month range calendar implemented. PostgreSQL overlap enforcement is in `AddAppointmentManagement`; no migration was applied automatically.

Clinical/EMR, laboratory, pharmacy, billing, admission, staffing and reporting modules are not implemented yet.

## Authentication setup

Configure the local PostgreSQL connection using ASP.NET Core User Secrets:

```bash
dotnet user-secrets set "ConnectionStrings:HospitalDatabase" "Host=localhost;Database=hospital_management_dev;Username=hospital_app_user;Password=<password>" --project src/HospitalManagementSystem.Web
```

Apply migrations manually only after review. For Stage 6 verification, target a disposable database. Set the intended connection in the ConnectionStrings__HospitalDatabase environment variable first. The Testing environment uses that connection and skips application role initialization during EF tooling.

```bash
dotnet ef database update AddAppointmentBookingFingerprint --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext -- --environment Testing
```

`AddAppointmentManagement` enables PostgreSQL's `btree_gist` extension and adds a GiST exclusion constraint. The migration account needs permission to create the extension; a DBA may install it beforehand with `CREATE EXTENSION btree_gist;`. Do not grant the application login superuser privileges just for this migration.

The initial appointment policy uses Asia/Colombo display/input time, UTC database instants, 15-minute start increments and durations of 15, 30, 45 or 60 minutes. Appointments use end-exclusive `[start,end)` ranges. Scheduled and CheckedIn appointments block overlaps; Cancelled and Missed appointments release their slots. Completed records remain historical and are not rescheduled.

The new fingerprint migration preserves existing appointment rows and scopes each booking key to the submitting user. Identical retries return the original appointment ID even after rescheduling. A changed request is rejected. Older bookings remain usable, but their old request keys cannot be replayed because the original payload cannot be recovered reliably. See [database design](docs/database-design.md) for lock ordering and lifecycle details.

Create the first Administrator locally after migrations:

```bash
dotnet run --project src/HospitalManagementSystem.Web -- --bootstrap-first-admin
```

The bootstrap reads the password without echo. It does not accept passwords as command-line arguments and refuses if an active Administrator or the email already exists.

No Administrator account or password is seeded automatically.

## Run and test

```bash
dotnet restore
dotnet build
dotnet run --project src/HospitalManagementSystem.Web
dotnet test HospitalManagementSystem.slnx
```

Open `/Account/Login`. `/Dashboard` requires authentication and `/Users` requires the Administrator role.

The standard suite uses isolated EF Core InMemory databases. To run the opt-in PostgreSQL integration test, provision a disposable PostgreSQL 18 database with a name containing `test`, and configure its connection securely in `HOSPITAL_APPOINTMENT_TEST_CONNECTION`. Never use `hospital_management_dev` or a shared database. The test inserts and removes fictional fixtures and expects the full migration chain to be applied already.

In PowerShell, after configuring that variable, confirm the target and run:

```powershell
$env:ConnectionStrings__HospitalDatabase = $env:HOSPITAL_APPOINTMENT_TEST_CONNECTION
dotnet ef database update AddAppointmentBookingFingerprint --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext -- --environment Testing
dotnet test HospitalManagementSystem.slnx --filter FullyQualifiedName~PostgreSqlAppointmentIntegrationTests
Remove-Item Env:ConnectionStrings__HospitalDatabase
Remove-Item Env:HOSPITAL_APPOINTMENT_TEST_CONNECTION
```

The PostgreSQL test covers adjacent and conflicting bookings, concurrent identical retries, opposite-direction rescheduling, database constraint violations, and transaction rollback. The normal suite skips it when the test connection is absent. Browser-delivered password reset email remains unimplemented until an email provider is configured.

### Appointment browser checklist

Use a separate demo database and fictional records. Configure the demo connection, then start the application with `dotnet run --project src/HospitalManagementSystem.Web` and open the HTTPS address shown in the terminal.

1. Sign in as an Administrator or Receptionist. Open **Appointments ? Book appointment**. Switch between two departments and confirm the doctor selection resets and lists only active doctors in the selected department.
2. Choose a future workday where the schedule starts at 09:10. Confirm that 09:15 is the first available 15-minute-aligned start, and change the duration to confirm that the slots update.
3. Book a slot, then check the confirmation and directory. Open the same form in another browser session and confirm a competing booking receives a helpful error. Verify an adjacent booking is accepted.
4. Reschedule to a doctor in another department. Open the same appointment in two tabs and confirm a stale save asks you to reload. Cancel with a reason and confirm the record remains visible with its new status.
5. Set **Upcoming only** and other filters. Page forward and backward. Verify that the filters and sort stay in effect.
6. Open the calendar and try the day, week and month views. Sign in with a linked Doctor account and verify that only that doctor's appointments appear and that opening another doctor's appointment is denied.

## Persistent security configuration

For deployments, persist ASP.NET Core Data Protection keys outside the application directory using a protected key store or an approved cloud key store. Losing keys invalidates authentication cookies and protected data.

## Security

Only fictional demonstration data will be used. Credentials and sensitive information must not be committed to the repository.

## Patient Document Management

Configure private patient documents with a persistent location outside `wwwroot`:

```bash
dotnet user-secrets set "PatientDocuments:StorageRoot" "C:\\HospitalManagementData\\PatientDocuments" --project src/HospitalManagementSystem.Web
dotnet user-secrets set "PatientDocuments:MaxFileSizeBytes" "10485760" --project src/HospitalManagementSystem.Web
```

Do not deploy document uploads publicly without private persistent storage and malware scanning. Production storage should use encrypted persistent volumes and restricted OS/application permissions.
