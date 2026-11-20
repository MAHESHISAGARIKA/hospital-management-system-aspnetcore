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

Clinical, appointment, laboratory, pharmacy, billing, admission, staffing and reporting modules are not implemented yet.

## Authentication setup

Configure the local PostgreSQL connection using ASP.NET Core User Secrets:

```bash
dotnet user-secrets set "ConnectionStrings:HospitalDatabase" "Host=localhost;Database=hospital_management_dev;Username=hospital_app_user;Password=<password>" --project src/HospitalManagementSystem.Web
```

Apply migrations manually only after review:

```bash
dotnet ef database update InitialDatabaseFoundation --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext
dotnet ef database update AddIdentityAndAccessControl --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext
dotnet ef database update AddPatientManagement --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --context HospitalDbContext
```

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
dotnet test src/HospitalManagementSystem.Web.Tests/HospitalManagementSystem.Web.Tests.csproj
```

Open `/Account/Login`. `/Dashboard` requires authentication and `/Users` requires the Administrator role.

The automated suite uses EF Core InMemory isolation and never resets or writes to `hospital_management_dev`. Browser-delivered password reset email remains unimplemented until an email provider is configured.

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
