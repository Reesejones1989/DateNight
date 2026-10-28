using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DateNight.Server.Data;

/// <summary>
/// Picks the database: SQLite on your computer, Postgres (e.g. Neon) when deployed.
/// The choice is made from the connection string, so nothing else needs to change.
/// </summary>
public static class DatabaseSetup
{
    public const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public static bool IsPostgres(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        if (IsPostgres(connectionString))
            options.UseNpgsql(ToNpgsqlFormat(connectionString));
        else
            options.UseSqlite(connectionString);
    }

    /// <summary>
    /// Neon and most hosts hand out URLs like postgresql://user:pass@host/db?sslmode=require,
    /// but Npgsql wants "Host=...;Username=...". Converts the first into the second.
    /// </summary>
    public static string ToNpgsqlFormat(string connectionString)
    {
        if (!connectionString.Contains("://")) return connectionString;

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var parts = new List<string>
        {
            $"Host={uri.Host}",
            $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
            $"Database={Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'))}",
            $"Username={Uri.UnescapeDataString(userInfo[0])}",
        };
        if (userInfo.Length > 1) parts.Add($"Password={Uri.UnescapeDataString(userInfo[1])}");

        var query = QueryHelpers.ParseQuery(uri.Query);
        var sslMode = query.TryGetValue("sslmode", out var s) ? s.ToString() : "require";
        parts.Add("SSL Mode=" + (sslMode.ToLowerInvariant() switch
        {
            "disable" => "Disable",
            "allow" => "Allow",
            "prefer" => "Prefer",
            "verify-ca" => "VerifyCA",
            "verify-full" => "VerifyFull",
            _ => "Require"
        }));
        if (query.TryGetValue("channel_binding", out var cb))
            parts.Add("Channel Binding=" + (cb.ToString().ToLowerInvariant() switch
            {
                "disable" => "Disable",
                "require" => "Require",
                _ => "Prefer"
            }));

        // Neon pauses the database when idle; give the first connection time to wake it.
        parts.Add("Timeout=30");
        return string.Join(';', parts);
    }
}
