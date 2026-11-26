# Deployment guide

Use .NET 10 and PostgreSQL 18, a dedicated application service account, and an explicitly selected staging database. Use a separate schema owner or migration role. Grant the runtime role access to required tables and sequences with no database creation or schema ownership. On Windows Server, install the ASP.NET Core Hosting Bundle for IIS. On Linux, use the matching ASP.NET Core runtime and a service manager behind an HTTPS reverse proxy.

## Build and database preparation

```powershell
dotnet publish src/HospitalManagementSystem.Web -c Release -o artifacts/publish
$env:ConnectionStrings__HospitalDatabase = 'Host=127.0.0.1;Port=1;Database=design_only'
dotnet ef migrations script --idempotent --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --output artifacts/hms-migrations.sql -- --environment Testing
Remove-Item Env:ConnectionStrings__HospitalDatabase
```

Review the script and archive a tested backup of the intended database plus patient documents before deploying. In the controlled migration window, confirm the host, port and database with psql `\conninfo`. Execute the reviewed script using `psql -X -v ON_ERROR_STOP=1 --file artifacts/hms-migrations.sql` and the explicit connection arguments for the approved target. The existing migrations use PostgreSQL's `btree_gist` extension. The live runtime account needs to read migration history, and startup validates or creates the predefined Identity roles. No Stage 14 schema migration is required.

## Runtime configuration

| Key | Setting |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Use Production for staging acceptance and deployment. |
| `ConnectionStrings__HospitalDatabase` | Inject the runtime credential from the platform secret store. Require certificate-validated TLS for remote PostgreSQL. |
| `AllowedHosts` | Set the approved host names for the deployment. |
| `Hosting__TrustedProxy` | Configure the immediate proxy IP when that proxy supplies forwarded headers. Let IIS integration handle forwarding where applicable. |
| `DataProtection__KeyDirectory` | Existing, absolute, access-controlled directory for persistent Identity cookie and token keys. Use a durable encrypted volume. |
| `PatientDocuments__StorageRoot` | Absolute path to protected, writable patient document storage outside the web root. |
| `Logging__LogLevel__Default` | Use a controlled level and restrict access to logs. |

Configure the HTTPS certificate at IIS or the reverse proxy, bind Kestrel to a private endpoint, and preserve the original HTTPS scheme through the trusted hop. Restrict file permissions on the publish directory, secret store, document storage and key ring. Grant runtime write permissions only where required. Persist and back up the key ring independently. Configuring a new key repository or changing the application discriminator may require users to sign in again. An existing Identity Administrator must be available before rollout. Use the explicit interactive [Administrator bootstrap](railway-deployment.md#provision-the-first-administrator) for the Railway demonstration database.

## Smoke tests and rollback

Start the hosted application with the intended runtime identity. Check HTTPS and HSTS, `/health/live`, `/health/ready`, login, logout, role navigation, protected report export, and one fictional checkout or clinical workflow in staging. Inspect logs for migration, permission and startup errors. Check restart behavior and that the configured account retains access.

Keep the previous application package and configuration. Roll back the binary if it is compatible with the current schema. If schema recovery is necessary, stop writes and restore a verified backup into a fresh database. Validate it before switching the connection. Reverse EF migrations can drop tables and should be treated as a data-loss operation requiring separate review.
