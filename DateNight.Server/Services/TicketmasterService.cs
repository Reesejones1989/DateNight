using System.Globalization;
using System.Text.Json;
using DateNight.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace DateNight.Server.Services;

/// <summary>Searches ticketed events with the Ticketmaster Discovery API.</summary>
public class TicketmasterService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<TicketmasterService> log)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Ticketmaster:ApiKey"]);

    public async Task<List<EventResult>> SearchAsync(
        GeoPoint center, int radiusMiles, DateOnly from, DateOnly to, string? keyword, string? category, CancellationToken ct)
    {
        var apiKey = config["Ticketmaster:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new();

        // Pad the UTC window so evening events in US time zones aren't cut off, then filter by local date below.
        var startUtc = from.ToDateTime(TimeOnly.MinValue).AddHours(-12);
        var endUtc = to.ToDateTime(TimeOnly.MaxValue).AddHours(14);

        var q = new Dictionary<string, string?>
        {
            ["apikey"] = apiKey,
            ["geoPoint"] = GeoUtils.Geohash(center.Latitude, center.Longitude, 7),
            ["radius"] = Math.Clamp(radiusMiles, 1, 150).ToString(CultureInfo.InvariantCulture),
            ["unit"] = "miles",
            ["startDateTime"] = startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["endDateTime"] = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["sort"] = "date,asc",
            ["size"] = "100",
            ["keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            ["classificationName"] = string.IsNullOrWhiteSpace(category) ? null : category,
        };
        var url = "https://app.ticketmaster.com/discovery/v2/events.json?" +
                  string.Join("&", q.Where(kv => kv.Value != null).Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));

        var cacheKey = "tm:" + url;
        if (cache.TryGetValue(cacheKey, out List<EventResult>? cached)) return cached!;

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Ticketmaster returned {Status}", resp.StatusCode);
            throw new HttpRequestException($"Ticketmaster returned {(int)resp.StatusCode}. Check your API key.");
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var results = new List<EventResult>();

        if (doc.RootElement.TryGetProperty("_embedded", out var emb) && emb.TryGetProperty("events", out var events))
        {
            foreach (var ev in events.EnumerateArray())
            {
                var r = Map(ev, center);
                if (r is null) continue;
                if (r.StartTime is { } st && (DateOnly.FromDateTime(st) < from || DateOnly.FromDateTime(st) > to)) continue;
                results.Add(r);
            }
        }

        // Ticketmaster often lists the same show several times (e.g. VIP packages); keep one per name+time.
        results = results.GroupBy(r => (r.Title, r.StartTime, r.VenueName)).Select(g => g.First()).ToList();

        cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
        return results;
    }

    private static EventResult? Map(JsonElement ev, GeoPoint center)
    {
        string? Str(JsonElement e, params string[] path)
        {
            foreach (var p in path)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out var next)) return null;
                e = next;
            }
            return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : null;
        }

        var id = Str(ev, "id");
        var name = Str(ev, "name");
        if (id is null || name is null) return null;

        DateTime? start = null;
        var localDate = Str(ev, "dates", "start", "localDate");
        var localTime = Str(ev, "dates", "start", "localTime");
        if (localDate != null && DateTime.TryParse($"{localDate} {localTime ?? "00:00:00"}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            start = dt;

        JsonElement venue = default;
        if (ev.TryGetProperty("_embedded", out var emb) && emb.TryGetProperty("venues", out var venues) && venues.GetArrayLength() > 0)
            venue = venues[0];

        double? lat = null, lon = null;
        if (venue.ValueKind == JsonValueKind.Object &&
            double.TryParse(Str(venue, "location", "latitude"), CultureInfo.InvariantCulture, out var la) &&
            double.TryParse(Str(venue, "location", "longitude"), CultureInfo.InvariantCulture, out var lo))
        {
            lat = la; lon = lo;
        }

        decimal? min = null, max = null; string? currency = null;
        if (ev.TryGetProperty("priceRanges", out var prices) && prices.GetArrayLength() > 0)
        {
            var p = prices[0];
            if (p.TryGetProperty("min", out var mn) && mn.TryGetDecimal(out var mnv)) min = mnv;
            if (p.TryGetProperty("max", out var mx) && mx.TryGetDecimal(out var mxv)) max = mxv;
            currency = Str(p, "currency");
        }

        string? image = null;
        if (ev.TryGetProperty("images", out var imgs))
        {
            image = imgs.EnumerateArray()
                .Where(i => Str(i, "ratio") == "16_9")
                .OrderBy(i => Math.Abs((i.TryGetProperty("width", out var w) ? w.GetInt32() : 0) - 640))
                .Select(i => Str(i, "url"))
                .FirstOrDefault() ?? imgs.EnumerateArray().Select(i => Str(i, "url")).FirstOrDefault();
        }

        string? category = null;
        if (ev.TryGetProperty("classifications", out var cls) && cls.GetArrayLength() > 0)
        {
            var segment = Str(cls[0], "segment", "name");
            var genre = Str(cls[0], "genre", "name");
            category = string.Join(" · ", new[] { segment, genre }.Where(s => !string.IsNullOrWhiteSpace(s) && s != "Undefined"));
        }

        return new EventResult
        {
            ExternalId = id,
            Source = ItemSource.Ticketmaster,
            Title = name,
            Category = string.IsNullOrEmpty(category) ? null : category,
            StartTime = start,
            VenueName = venue.ValueKind == JsonValueKind.Object ? Str(venue, "name") : null,
            Address = venue.ValueKind == JsonValueKind.Object ? Str(venue, "address", "line1") : null,
            City = venue.ValueKind == JsonValueKind.Object ? Str(venue, "city", "name") : null,
            Latitude = lat,
            Longitude = lon,
            DistanceMiles = lat.HasValue ? Math.Round(GeoUtils.DistanceMiles(center.Latitude, center.Longitude, lat.Value, lon!.Value), 1) : null,
            IsTicketed = true,
            PriceMin = min,
            PriceMax = max,
            Currency = currency,
            Url = Str(ev, "url"),
            ImageUrl = image
        };
    }
}
