using Npgsql;

namespace HospitalManagementSystem.Web.Services;

public static class HospitalDatabaseConfiguration
{
    public static string Resolve(IConfiguration configuration)
    {
        var explicitConnection = configuration.GetConnectionString("HospitalDatabase");
        if (!string.IsNullOrWhiteSpace(explicitConnection))
        {
            if (!string.IsNullOrEmpty(configuration["RAILWAY_SERVICE_ID"]))
            {
                NpgsqlConnectionStringBuilder parsed;
                try { parsed = new NpgsqlConnectionStringBuilder(explicitConnection); }
                catch (ArgumentException) { throw new InvalidOperationException("The Railway database override is invalid."); }
                ValidateRailwayTarget(parsed);
            }
            return explicitConnection;
        }

        var host = configuration["PGHOST"];
        var database = configuration["PGDATABASE"];
        var user = configuration["PGUSER"];
        var password = configuration["PGPASSWORD"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password) ||
            !int.TryParse(configuration["PGPORT"], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Configure ConnectionStrings:HospitalDatabase or all five PostgreSQL connection variables with a valid port.");

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = host, Port = port, Database = database, Username = user, Password = password
        };
        if (!string.IsNullOrEmpty(configuration["RAILWAY_SERVICE_ID"])) ValidateRailwayTarget(connection);
        return connection.ConnectionString;
    }

    private static void ValidateRailwayTarget(NpgsqlConnectionStringBuilder connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
            string.Equals(connection.Database, "hospital_management_dev", StringComparison.OrdinalIgnoreCase) ||
            !connection.Host.EndsWith(".railway.internal", StringComparison.OrdinalIgnoreCase) ||
            Uri.CheckHostName(connection.Host) != UriHostNameType.Dns ||
            connection.IncludeErrorDetail || connection.LogParameters || connection.PersistSecurityInfo)
            throw new InvalidOperationException("Railway requires a private railway.internal demo database with sensitive logging disabled.");
    }
}
