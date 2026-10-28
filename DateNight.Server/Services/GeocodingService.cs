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
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Nominatim returned {Status} for {Query}", (int)resp.StatusCode, query);
                throw new HttpRequestException((int)resp.StatusCode switch
                {
                    403 or 406 => "OpenStreetMap's location service refused the request. Set OpenStreetMap:ContactEmail (see README) and try again.",
                    429 => "OpenStreetMap's location service is rate-limiting this app. Wait a minute and try again.",
                    _ => $"OpenStreetMap's location service returned {(int)resp.StatusCode}. Try again in a minute."
                });
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
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
        catch (HttpRequestException ex) when (ex.StatusCode is null && ex.InnerException is not null)
        {
            log.LogWarning(ex, "Geocoding service unreachable for {Query}", query);
            throw new HttpRequestException("Couldn't reach the OpenStreetMap location service. Check your internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Geocoding timed out for {Query}", query);
            throw new HttpRequestException("The OpenStreetMap location service took too long to answer. Try again.", ex);
        }
    }
}
