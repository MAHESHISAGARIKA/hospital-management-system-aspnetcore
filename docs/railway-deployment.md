# Railway demonstration deployment

Use a dedicated Railway project and environment with only the web and PostgreSQL services you control. Use fictional records and individual demo accounts. The HTTPS URL is publicly reachable. Identity restricts application access, and the URL itself provides no access control.

## Railway dashboard

1. Sign up at Railway and connect GitHub. Create a project and add PostgreSQL. Keep its database volume, and remove any PostgreSQL public TCP proxy under **Settings ? Networking**. Verify PostgreSQL 18 and the availability of `btree_gist`.
2. Add a web service from the GitHub repository and select the reviewed deployment branch. Use the repository root as the build context. Railway discovers the root `Dockerfile`. Leave custom start and pre-deploy commands empty. Hold publication and automatic redeployments until setup is complete.
3. Attach a separate volume to the web service at `/data`. Use one replica. Set the variables below. The web image can build before schema setup, but the service needs the schema to start.
4. Prepare the schema and Administrator through the private tunnel below. Use a dedicated database runtime role after provisioning.
5. Set the service health check to `/health/ready`. Generate a Railway domain under **Settings ? Networking ? Public Networking**. Configure its allowlist and edge mode, then deploy. Use the application?s dashboard and login from the generated HTTPS address. Keep the PostgreSQL service private.

| Web service variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `PORT` | Railway injects it. If selecting a fixed value, use `8080` and match the domain target port. |
| `PGHOST` | Reference the PostgreSQL private host, for example `${{Postgres.PGHOST}}`. Verify it ends in `.railway.internal`. |
| `PGPORT` | `${{Postgres.PGPORT}}`; use the internal port. |
| `PGDATABASE` | `${{Postgres.PGDATABASE}}`; verify the isolated demo database name. |
| `PGUSER`, `PGPASSWORD` | Private runtime login and secret. Reference the database service variables only if those are scoped to the intended role. |
| `RAILWAY_RUN_UID` | `0` for Railway volume initialization. The entrypoint drops to Microsoft's non-root `app` user before starting .NET. |
| `DataProtection__KeyDirectory` | `/data/keys`, set by the entrypoint on Railway. |
| `PatientDocuments__StorageRoot` | `/data/documents`, set by the entrypoint on Railway. |
| `AllowedHosts` | Generated host followed by `;healthcheck.railway.app`. |
| `Hosting__RailwayEdgeOnly` | `true` only after the generated domain exists and the environment meets the isolation requirements below. |

`ConnectionStrings__HospitalDatabase` is an alternative and overrides the five PG variables. Leave it unset if using the PG variables. The application constructs a connection string with Npgsql and refuses the local development database name and public hosts on Railway. Use the private encrypted network. Keep secrets in Railway variables and avoid printing configuration or enabling parameter logging.

### HTTPS trust and storage

The isolated demo mode uses the fixed Railway-generated `RAILWAY_PUBLIC_DOMAIN` as the HTTPS origin for requests with that exact Host and no port or port 443. It ignores forwarded headers and relies on Railway edge TLS for public traffic. Private services can reach Kestrel directly, so enable this mode only in a dedicated environment with trusted services, no web or database TCP proxies, and no untrusted workloads. Domain matching alone cannot prove a request traversed the edge. Do not use this mode on a shared or untrusted private network. Railway does not publish stable ingress proxy IPs. For another hosting topology, leave edge mode off and use `Hosting__TrustedProxy` with a verified immediate proxy IP. Keep unrestricted forwarded-header switches off. See [Railway edge behavior](https://docs.railway.com/networking/public-networking/specs-and-limits) and [Railway proxy trust guidance](https://station.railway.com/questions/asp-net-core-10-secure-forwarded-heade-506a954b).

Railway mounts the volume as root. The entrypoint initializes only the two application directories with mode `0700`, owned by `app`, then drops privileges. Keep the same volume and application discriminator across redeploys to preserve Identity cookies. Back up keys and documents as confidential data separately from PostgreSQL. Filesystem key persistence relies on volume access controls and does not add application-level encryption of the key files. Expect brief redeployment downtime with one volume. See [Railway volume permissions](https://docs.railway.com/volumes).

## Prepare the database once

Generate the reviewed SQL locally with the repository?s EF CLI. The offline placeholder points to an unusable port.

```powershell
$env:ConnectionStrings__HospitalDatabase = 'Host=127.0.0.1;Port=1;Database=design_only'
try {
    dotnet ef migrations script --idempotent --project src/HospitalManagementSystem.Web --startup-project src/HospitalManagementSystem.Web --output artifacts/railway-schema.sql -- --environment Testing
} finally {
    Remove-Item Env:ConnectionStrings__HospitalDatabase
}
```

Review all 12 migration entries from `InitialDatabaseFoundation` through `AddStaffManagement`, including extension, trigger and constraint SQL. In Railway, obtain the PostgreSQL deployment instance ID and configure your Railway SSH key with `railway ssh keys add` or in the dashboard. From your workstation, open a local tunnel to the database's loopback port through that deployment. Replace the instance ID and use the actual internal PostgreSQL port.

```powershell
ssh -N -o ExitOnForwardFailure=yes -L 127.0.0.1:15432:127.0.0.1:5432 <postgres-instance-id>@ssh.railway.com
```

With the tunnel open, use the database owner or migration role and an interactive password prompt. Replace `$demoOwner` and `$demoDatabase` with verified Railway demo identifiers. In the first psql connection, use `\conninfo`, `SELECT current_database(), current_user, version();`, and check that the target contains no application tables. Stop if the environment, database, or role differs from your intended target.

```powershell
psql -X -W -h 127.0.0.1 -p 15432 -U $demoOwner -d $demoDatabase
psql -X -W -h 127.0.0.1 -p 15432 -U $demoOwner -d $demoDatabase -v ON_ERROR_STOP=1 -f artifacts/railway-schema.sql
```

In a fresh psql session, run `SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";`. Expect 12 rows ending in `20261125153316_AddStaffManagement`. If extension creation is denied, have the database owner install `btree_gist` on this isolated database. Give the runtime role CONNECT, schema USAGE, required table CRUD and sequence access. Limit migration-history access to SELECT and keep DDL privileges with the migration role. Runtime startup checks or creates Identity roles and applies no migrations. See [Railway SSH forwarding](https://docs.railway.com/cli/ssh#forward-a-port-with-ssh--l).

For example, in the same confirmed demo database, use an owner session to create an application role. Replace the demo database identifier if you chose a different one. The password prompt keeps the value out of the SQL script.

```sql
CREATE ROLE hms_demo_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
\password hms_demo_runtime
GRANT CONNECT ON DATABASE railway TO hms_demo_runtime;
GRANT USAGE ON SCHEMA public TO hms_demo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hms_demo_runtime;
REVOKE INSERT, UPDATE, DELETE ON TABLE public."__EFMigrationsHistory" FROM hms_demo_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hms_demo_runtime;
```

Set the web service PGUSER and PGPASSWORD to this runtime account. Apply DDL as the owner and review privileges when adding tables in later releases.

## Provision the first Administrator

Use the same tunnel and intended database from a private interactive terminal. The command now supports Production, requires interactive input, serializes bootstrap attempts using a PostgreSQL transaction lock, refuses an existing Administrator and exits without starting HTTP. Avoid opening a public domain before provisioning. Run from the repository root using a securely injected process-scoped database password. Clear any explicit override first so the five PG variables select the tunnel.

```powershell
Remove-Item Env:ConnectionStrings__HospitalDatabase -ErrorAction SilentlyContinue
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ENVIRONMENT = 'Production'
$env:PGHOST = '127.0.0.1'
$env:PGPORT = '15432'
$env:PGDATABASE = $demoDatabase
$env:PGUSER = $demoRuntimeUser
$secret = Read-Host 'Railway demo database password' -AsSecureString
$env:PGPASSWORD = [pscredential]::new('demo', $secret).GetNetworkCredential().Password
try {
    dotnet run --no-launch-profile --project src/HospitalManagementSystem.Web -c Release -- --bootstrap-first-admin
} finally {
    Remove-Item Env:PGHOST,Env:PGPORT,Env:PGDATABASE,Env:PGUSER,Env:PGPASSWORD,Env:ASPNETCORE_ENVIRONMENT,Env:DOTNET_ENVIRONMENT
    $secret.Dispose()
}
```

Enter a private, unique admin password at the prompt. Sign in over the generated HTTPS URL, then create least-privilege supervisor accounts with fictional addresses using **Manage users**. Share temporary credentials privately and complete the first-sign-in password change. Keep Administrator credentials private. See the [supervisor demo](supervisor-demo.md).

## Verify and redeploy

- Check `/health/live` for process status and `/health/ready` for DB connectivity and migration history. These endpoints return generic status. Railway uses readiness during deployment.
- Verify the HTTPS login and dashboard, Secure and HttpOnly cookies, and a denied unauthorized route. Confirm the session survives a redeploy using the same volume. Check document persistence if demonstrating uploads.
- For readiness failures, inspect Railway build and deploy logs for missing schema, private host configuration, or volume permission errors. For HTTP 400 check AllowedHosts, including Railway?s health host. For HTTP 503 on the public app with healthy readiness, check the generated domain and edge mode. Avoid enabling sensitive logs.
- For later UI changes, deploy a reviewed GitHub commit to the same service. Keep the same database and volume. Review any schema changes separately. Run login and readiness checks before sharing the updated build.
- Check current Railway billing and available volumes before enabling services. Railway lists limited usage credit and a single volume on its Free plan. This layout needs separate PostgreSQL and web volumes, so confirm your account supports both. Watch resource, storage and egress usage, trial expiry and idle behavior. See [Railway Free plan](https://docs.railway.com/pricing/free-plan) and [Railway pricing](https://docs.railway.com/pricing/plans).

Hosted login, proxy behavior, volume remount permissions, backup recovery and the seven opt-in PostgreSQL tests remain release checks. The [release-readiness record](release-readiness.md) lists earlier verification.
