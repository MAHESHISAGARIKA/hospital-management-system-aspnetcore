# Security review

Identity requires twelve-character passwords with complexity checks. Five failed sign-in attempts trigger a timed lockout. Authentication cookies are HTTP-only with SameSite Lax and secure outside Development. Sessions slide within the configured interval. The password-reset token lifespan is thirty minutes. The authenticated MVC suites cover CSRF, resource ownership, and protected reports for all seven roles. The centralized PostgreSQL test guard prevents accidental use of application database settings.

Clinical access depends on an active linked provider or care assignment. Administrator user management does not grant general access to clinical narratives. Accounting reports require the Accountant role. Patient and staff private content stays out of summary CSV exports. Audit entries record actors and operations, and tests check leave-reason and export redaction.

Deployment should use a validated HTTPS endpoint, explicitly trusted proxy, isolated key storage, restricted service identity and an approved secret store. The production exception handler returns the generic error view. Health responses report a generic status and readiness verifies database connectivity and pending migrations. Production startup requires the database for Identity role initialization.

Remaining verification includes exercising HTTPS, forwarded headers and sign-out on the selected host, checking protected storage and database permissions, and testing recovery. The direct and transitive NuGet advisory check reported no vulnerable packages for either project from the configured nuget.org source. The review is based on tracked configuration and source files. It is not a formal penetration test or a credential rotation review.
