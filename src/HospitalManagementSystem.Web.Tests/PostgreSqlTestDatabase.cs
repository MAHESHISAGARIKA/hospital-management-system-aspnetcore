using Npgsql;
using Xunit;

namespace HospitalManagementSystem.Web.Tests;

internal static class PostgreSqlTestDatabase
{
    internal const string OptInVariable = "HOSPITAL_TEST_POSTGRES_OPT_IN";
    internal const string ConnectionVariable = "HOSPITAL_APPOINTMENT_TEST_CONNECTION";
    private const string Database = "hospital_management_integration_test";
    private const string Role = "hms_integration_test";

    internal static bool OptedIn => Environment.GetEnvironmentVariable(OptInVariable) == "1";

    internal static NpgsqlConnectionStringBuilder Validate(string raw)
    {
        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(raw); }
        catch (ArgumentException) { throw new InvalidOperationException("The PostgreSQL test connection configuration is invalid."); }
        if (connection.Database != Database || connection.Username != Role ||
            connection.Host is not ("127.0.0.1" or "::1") || connection.PersistSecurityInfo ||
            connection.IncludeErrorDetail || connection.LogParameters)
            throw new InvalidOperationException("Use the dedicated loopback PostgreSQL test database and restricted test role with safe logging settings.");
        return connection;
    }

    internal static async Task<string> VerifiedConnectionStringAsync()
    {
        if (!OptedIn) throw new InvalidOperationException("Explicit PostgreSQL test opt-in is required.");
        var raw = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("The PostgreSQL test connection is missing.");
        var settings = Validate(raw);
        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT current_database() = @database
                    AND current_user = @role AND session_user = @role
                    AND shobj_description(d.oid, 'pg_database') = 'HMS disposable integration test database'
                    AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole
                    AND NOT r.rolreplication AND NOT r.rolbypassrls AND NOT r.rolinherit
                    AND NOT EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = r.oid)
                FROM pg_database d JOIN pg_roles r ON r.rolname = current_user
                WHERE d.datname = current_database()
                """, connection);
            command.Parameters.AddWithValue("database", Database);
            command.Parameters.AddWithValue("role", Role);
            if (await command.ExecuteScalarAsync() is true) return settings.ConnectionString;
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL test target verification could not connect or read the database and role identity.");
        }
        throw new InvalidOperationException("PostgreSQL test target failed the server-side database marker or restricted role check.");
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (!PostgreSqlTestDatabase.OptedIn)
            Skip = $"Set {PostgreSqlTestDatabase.OptInVariable}=1 and configure the dedicated isolated PostgreSQL test database.";
    }
}

