using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace DateNight.Server.Services;

public record GeoPoint(string Name, double Latitude, double Longitude);

/// <summary>Turns a city name into coordinates using OpenStreetMap's free Nominatim service.</summary>
public class GeocodingService(HttpClient http, IMemoryCache cache, ILogger<GeocodingService> log)
{
    public async Task<GeoPoint?> GeocodeAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length == 0) return null;

        var key = "geo:" + query.ToLowerInvariant();
        if (cache.TryGetValue(key, out GeoPoint? cached)) return cached;

        try
        {
            var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&addressdetails=0&q={Uri.EscapeDataString(query)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return null;

            var point = new GeoPoint(
                first.GetProperty("display_name").GetString() ?? query,
                double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture));

            cache.Set(key, point, TimeSpan.FromDays(7));
            return point;
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Unexpected geocoding response for {Query}", query);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Geocoding service unreachable for {Query}", query);
            throw new HttpRequestException("Couldn't reach the OpenStreetMap location service. Check your internet connection and try again.", ex);
        }
    }
}
