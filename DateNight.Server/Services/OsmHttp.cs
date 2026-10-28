namespace DateNight.Server.Services;

/// <summary>
/// Request headers for OpenStreetMap's free services (Nominatim and Overpass).
/// Both now reject requests that don't identify the app, ideally with a contact address:
/// https://operations.osmfoundation.org/policies/nominatim/ and
/// https://wiki.openstreetmap.org/wiki/Overpass_API#Public_Overpass_API_instances
/// </summary>
public static class OsmHttp
{
    public static void Configure(HttpClient client, IConfiguration config, TimeSpan timeout)
    {
        var contact = config["OpenStreetMap:ContactEmail"];
        var userAgent = string.IsNullOrWhiteSpace(contact)
            ? "DateNight/1.0 (date-night itinerary planner)"
            : $"DateNight/1.0 (date-night itinerary planner; {contact.Trim()})";

        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        // On Render, RENDER_EXTERNAL_URL is the site's public address (set automatically).
        // A custom OpenStreetMap:Referer wins; otherwise use the Render address, then the localhost default.
        var configured = config["OpenStreetMap:Referer"];
        var referer = !string.IsNullOrWhiteSpace(configured) && !configured.Contains("localhost")
            ? configured
            : config["RENDER_EXTERNAL_URL"] ?? configured;
        if (Uri.TryCreate(string.IsNullOrWhiteSpace(referer) ? "http://localhost/" : referer, UriKind.Absolute, out var r))
            client.DefaultRequestHeaders.Referrer = r;
        client.Timeout = timeout;
    }
}
