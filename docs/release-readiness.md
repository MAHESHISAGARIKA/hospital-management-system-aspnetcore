# Release readiness

| Gate | Status | Evidence or open item |
| --- | --- | --- |
| Code quality | PASS | Restore succeeded. Debug and Release each built with zero warnings and errors. |
| Testing | BLOCKED | Debug and Release each reported 152 passed, 0 failed and 7 skipped out of 159. The skipped cases require explicit opt-in and the guarded isolated database. |
| Security | NOT VERIFIED | Automated coverage exercises all seven roles. NuGet reported no vulnerable dependencies. Hosted HTTPS, secret-store and deployed permission checks remain open. |
| Database | NOT VERIFIED | Generated idempotent SQL offline for all twelve migrations. The last migration matches the model. Live constraints require the isolated run. |
| Backup and recovery | NOT VERIFIED | Backup and restore procedure documented. Recovery drill remains open. |
| End-to-end functionality | NOT VERIFIED | Existing automated module coverage and manual cross-module acceptance checklist. |
| Release and deployment | NOT VERIFIED | Release publish to artifacts/publish succeeded. Loopback Testing smoke returned liveness 200, readiness 503 with an unreachable database, login 200 and Bootstrap 200. Hosted staging remains open. |

Use the [testing guide](final-testing.md), [backup and restore guide](backup-restore.md), and [deployment guide](deployment-guide.md) to capture the outstanding evidence. Measure query latency and row counts on fictional staging data before setting a production performance target. Current performance evidence is source review only. Staff navigation performs a scoped employee lookup, dashboard sections load sequentially, and some staff report selections cap records. Use a measured dataset and database query plans before adding indexes.

## Demonstration status

On 2026-11-26 (Asia/Colombo), Debug and Release each built with 0 warnings and 0 errors. Each normal test run reported 152 passed, 0 failed and 7 skipped out of 159.

The PostgreSQL opt-in attempt was reported to fail safety validation. Final demonstration preparation uses the normal automated suite with opt-in disabled. Manual browser results, a full restore drill, hosted HTTPS and staging checks, and production-scale performance remain NOT VERIFIED. See the [supervisor walkthrough](supervisor-demo.md) for the rehearsal and evidence checklist.
