namespace HospitalManagementSystem.Web.Services;

public static class RailwayDemoHosting
{
    public static string? GetEdgeHost(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Hosting:RailwayEdgeOnly")) return null;
        var host = configuration["RAILWAY_PUBLIC_DOMAIN"];
        if (string.IsNullOrWhiteSpace(configuration["RAILWAY_SERVICE_ID"]) ||
            string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) != UriHostNameType.Dns ||
            !host.EndsWith(".up.railway.app", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The isolated Railway edge mode requires a generated Railway public domain.");
        return host;
    }

    public static bool MatchesEdgeHost(HostString requestHost, string publicDomain) =>
        string.Equals(requestHost.Host, publicDomain, StringComparison.OrdinalIgnoreCase) &&
        requestHost.Port is null or 443;
}

