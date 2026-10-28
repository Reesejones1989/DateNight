using System.Globalization;
using System.Text.Json;
using DateNight.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace DateNight.Server.Services;

/// <summary>Finds non-ticketed date spots (parks, museums, restaurants, bars...) from OpenStreetMap via the Overpass API.</summary>
public class PlacesService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<PlacesService> log)
{
    // Public Overpass servers, tried in order. The main one is often overloaded, so fall back to the others.
    // List: https://wiki.openstreetmap.org/wiki/Overpass_API#Public_Overpass_API_instances
    private static readonly string[] DefaultServers =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    ];

    private string[] Servers =>
        config.GetSection("OpenStreetMap:OverpassUrls").Get<string[]>() is { Length: > 0 } custom ? custom : DefaultServers;

    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    {
        ["outdoors"] = "Outdoors",
        ["culture"] = "Arts & Culture",
        ["food"] = "Food & Dessert",
        ["nightlife"] = "Drinks & Nightlife",
    };

    // Overpass filters per category, plus a max radius (dense categories get a smaller one so queries stay fast).
    private static readonly Dictionary<string, (string[] Filters, double MaxMiles)> Queries = new()
    {
        ["outdoors"] = (new[]
        {
            "nwr[\"leisure\"~\"^(park|garden|nature_reserve)$\"][\"name\"]",
            "nwr[\"tourism\"~\"^(viewpoint|picnic_site)$\"][\"name\"]",
            "nwr[\"natural\"=\"beach\"][\"name\"]",
        }, 10),
        ["culture"] = (new[]
        {
            "nwr[\"tourism\"~\"^(museum|gallery|attraction|zoo|aquarium)$\"][\"name\"]",
            "nwr[\"amenity\"~\"^(theatre|arts_centre|planetarium)$\"][\"name\"]",
        }, 10),
        ["food"] = (new[]
        {
            "nwr[\"amenity\"~\"^(restaurant|cafe|ice_cream)$\"][\"name\"]",
        }, 3),
        ["nightlife"] = (new[]
        {
            "nwr[\"amenity\"~\"^(bar|pub|nightclub|cinema)$\"][\"name\"]",
        }, 5),
    };

    public async Task<(List<EventResult> Results, string? Warning)> SearchAsync(GeoPoint center, int radiusMiles, string category, CancellationToken ct)
    {
        if (!Queries.TryGetValue(category, out var spec)) spec = Queries["outdoors"];

        var miles = Math.Min(radiusMiles, spec.MaxMiles);
        string? warning = miles < radiusMiles ? $"Places in this category are limited to {miles:0} miles to keep results fast." : null;
        var meters = (int)(miles * 1609.34);
        var latlon = $"{center.Latitude.ToString(CultureInfo.InvariantCulture)},{center.Longitude.ToString(CultureInfo.InvariantCulture)}";

        var body = "[out:json][timeout:25];(" +
                   string.Concat(spec.Filters.Select(f => $"{f}(around:{meters},{latlon});")) +
                   ");out center tags 300;";

        var cacheKey = "osm:" + body;
        if (cache.TryGetValue(cacheKey, out List<EventResult>? cached)) return (cached!, warning);

        using var doc = await QueryAsync(body, ct);
        var results = new List<EventResult>();

        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var tags)) continue;
            string? Tag(string k) => tags.TryGetProperty(k, out var v) ? v.GetString() : null;

            var name = Tag("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            double lat, lon;
            if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lon = el.GetProperty("lon").GetDouble(); }
            else if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lon = c.GetProperty("lon").GetDouble(); }
            else continue;

            var kind = Tag("tourism") ?? Tag("leisure") ?? Tag("amenity") ?? Tag("natural");
            var street = string.Join(" ", new[] { Tag("addr:housenumber"), Tag("addr:street") }.Where(s => s != null));
            var cuisine = Tag("cuisine")?.Replace(';', ',').Replace('_', ' ');

            results.Add(new EventResult
            {
                ExternalId = $"{el.GetProperty("type").GetString()}/{el.GetProperty("id").GetInt64()}",
                Source = ItemSource.OpenStreetMap,
                Title = name,
                Category = Pretty(kind) + (cuisine is null ? "" : $" · {cuisine}"),
                Address = string.IsNullOrWhiteSpace(street) ? null : street,
                City = Tag("addr:city"),
                Latitude = lat,
                Longitude = lon,
                DistanceMiles = Math.Round(GeoUtils.DistanceMiles(center.Latitude, center.Longitude, lat, lon), 1),
                IsTicketed = Tag("fee") == "yes",
                Url = Tag("website") ?? Tag("contact:website") ?? $"https://www.openstreetmap.org/{el.GetProperty("type").GetString()}/{el.GetProperty("id").GetInt64()}",
            });
        }

        results = results
            .GroupBy(r => r.Title.ToLowerInvariant())
            .Select(g => g.OrderBy(r => r.DistanceMiles).First())
            .OrderBy(r => r.DistanceMiles)
            .Take(80)
            .ToList();

        cache.Set(cacheKey, results, TimeSpan.FromHours(6));
        return (results, warning);
    }

    /// <summary>Runs an Overpass query, moving on to the next server if one refuses, is overloaded or times out.</summary>
    private async Task<JsonDocument> QueryAsync(string query, CancellationToken ct)
    {
        var problems = new List<string>();
        foreach (var server in Servers)
        {
            var host = Uri.TryCreate(server, UriKind.Absolute, out var u) ? u.Host : server;
            try
            {
                using var resp = await http.PostAsync(server,
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query }), ct);
                var text = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    log.LogWarning("Overpass {Host} returned {Status}: {Body}", host, (int)resp.StatusCode, Trim(text));
                    problems.Add($"{host}: {(int)resp.StatusCode} {Explain((int)resp.StatusCode)}");
                    continue;
                }

                var doc = JsonDocument.Parse(text);
                // Overpass answers 200 even when the query timed out; the problem is in "remark".
                if (doc.RootElement.TryGetProperty("remark", out var remark) &&
                    remark.GetString() is { } r && r.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    log.LogWarning("Overpass {Host} remark: {Remark}", host, r);
                    problems.Add($"{host}: {r}");
                    doc.Dispose();
                    continue;
                }
                return doc;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Overpass {Host} failed", host);
                problems.Add($"{host}: {(ex is TaskCanceledException ? "timed out" : ex.Message)}");
            }
        }

        throw new HttpRequestException(
            "Couldn't load places from OpenStreetMap right now. Try a smaller radius or try again in a minute. (" +
            string.Join("; ", problems) + ")");
    }

    private static string Explain(int status) => status switch
    {
        406 => "(request refused; set OpenStreetMap:ContactEmail)",
        429 => "(too many requests; wait 30 seconds)",
        504 or 503 => "(server overloaded)",
        _ => ""
    };

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private static string Pretty(string? s) =>
        string.IsNullOrEmpty(s) ? "Place" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));
}
