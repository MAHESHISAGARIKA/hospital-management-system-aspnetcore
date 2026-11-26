# Backup and restore

Use PostgreSQL 18 client tools, an approved backup account, sufficient disk space and a directory outside the repository, such as `D:\HMS-Backups`. Use a PostgreSQL password file with restricted permissions or an interactive prompt. Keep the account's credentials out of command arguments. Verify the source host, port and database with psql `\conninfo`. Back up the configured patient-document storage separately at the same recovery point.

```powershell
$backup = Join-Path 'D:\HMS-Backups' ("hms-{0}.dump" -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ'))
if (Test-Path -LiteralPath $backup) { throw 'Archive already exists.' }
pg_dump --host $backupHost --port $backupPort --username $backupUser --dbname $backupDatabase --format custom --file $backup
if ($LASTEXITCODE -ne 0) { throw 'Backup failed. Quarantine the partial archive.' }
pg_restore --list $backup
if ($LASTEXITCODE -ne 0) { throw 'Archive table of contents could not be read.' }
Get-FileHash -Algorithm SHA256 -LiteralPath $backup
```

Save the checksum, UTC time, source identifier and tool versions in the backup record. Restrict archive access to recovery operators. Use encrypted storage and an encrypted off-host copy. Agree a schedule and retention period based on recovery objectives and privacy rules, then verify scheduled jobs and alert on failures. Custom-format archives are compressed and require separate encryption. A checksum and archive listing verify the file, and a completed restore verifies recovery.

## Restore drill

Use a separate loopback PostgreSQL instance and a fresh database named `hospital_management_restore_test`. Use a backup containing fictional data for drills. Create the empty target deliberately with a dedicated restore operator. Verify the server port and database using `\conninfo`. Query `current_database()` and confirm there are zero application tables in the target. Install `btree_gist` on the isolated target as its owner if required by the archive and local policy.

```powershell
$restoreDatabase = 'hospital_management_restore_test'
$restoreArgs = @('--host', '127.0.0.1', '--port', $restorePort, '--username', $restoreUser, '--dbname', $restoreDatabase)
$identity = psql @restoreArgs -X -A -t -v ON_ERROR_STOP=1 -c "SELECT current_database() || ':' || count(*) FROM pg_tables WHERE schemaname='public';"
if ($LASTEXITCODE -ne 0 -or $identity -ne 'hospital_management_restore_test:0') { throw 'Restore target is not the empty isolated database.' }
pg_restore @restoreArgs --single-transaction --exit-on-error --no-owner --no-privileges $backup
if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Record the failure and inspect the isolated target.' }
@'
SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
'@ | psql @restoreArgs -X -v ON_ERROR_STOP=1
psql @restoreArgs -X -v ON_ERROR_STOP=1 -c "SELECT to_regclass('public.patients'), to_regclass('public.appointments'), to_regclass('public.staff_employees');"
@'
SELECT count(*) FROM appointments a LEFT JOIN patients p ON p."Id" = a."PatientId" WHERE p."Id" IS NULL;
'@ | psql @restoreArgs -X -v ON_ERROR_STOP=1
```

Confirm twelve expected migration IDs, non-null required table lookups, zero orphan rows and expected fictional fixture counts. Check the restored document store separately. In an isolated staging app check readiness, sign-in and a representative workflow. Record elapsed time and failures. Preserve the source archive. Database restore testing and recovery time remain unverified until this procedure is executed successfully.

## Current recovery status

No backup or restore drill was executed during the final demonstration preparation. Recovery integrity and elapsed time are NOT VERIFIED.
