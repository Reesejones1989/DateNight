using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DateNight.Shared;

namespace DateNight.Services;

public class ApiException(string message) : Exception(message);

/// <summary>Typed wrapper around the DateNight server API.</summary>
public class ApiClient(HttpClient http)
{
    // ---- Search ----

    public Task<SearchResponse> SearchEventsAsync(string city, int radius, DateOnly from, DateOnly to, string? keyword, string? category) =>
        GetAsync<SearchResponse>(
            $"api/search/events?city={Esc(city)}&radius={radius}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" +
            (string.IsNullOrWhiteSpace(keyword) ? "" : $"&keyword={Esc(keyword)}") +
            (string.IsNullOrWhiteSpace(category) ? "" : $"&category={Esc(category)}"));

    public Task<SearchResponse> SearchPlacesAsync(string city, int radius, string category, DateOnly? date = null) =>
        GetAsync<SearchResponse>($"api/search/places?city={Esc(city)}&radius={radius}&category={Esc(category)}" +
                                 (date is { } d ? $"&date={d:yyyy-MM-dd}" : ""));

    // ---- Itineraries ----

    public Task<List<ItinerarySummaryDto>> GetItinerariesAsync() => GetAsync<List<ItinerarySummaryDto>>("api/itineraries");

    public Task<ItineraryDto> GetItineraryAsync(int id) => GetAsync<ItineraryDto>($"api/itineraries/{id}");

    public Task<ItineraryDto> CreateItineraryAsync(ItineraryRequest req) => SendAsync<ItineraryDto>(HttpMethod.Post, "api/itineraries", req);

    public Task<ItineraryDto> UpdateItineraryAsync(int id, ItineraryRequest req) => SendAsync<ItineraryDto>(HttpMethod.Put, $"api/itineraries/{id}", req);

    public Task DeleteItineraryAsync(int id) => SendAsync<object>(HttpMethod.Delete, $"api/itineraries/{id}", null);

    public Task<ItineraryItemDto> AddItemAsync(int itineraryId, ItineraryItemDto item) =>
        SendAsync<ItineraryItemDto>(HttpMethod.Post, $"api/itineraries/{itineraryId}/items", item);

    public Task<ItineraryItemDto> UpdateItemAsync(int itineraryId, ItineraryItemDto item) =>
        SendAsync<ItineraryItemDto>(HttpMethod.Put, $"api/itineraries/{itineraryId}/items/{item.Id}", item);

    public Task DeleteItemAsync(int itineraryId, int itemId) =>
        SendAsync<object>(HttpMethod.Delete, $"api/itineraries/{itineraryId}/items/{itemId}", null);

    // ---- Plumbing ----

    private static string Esc(string s) => Uri.EscapeDataString(s.Trim());

    private async Task<T> GetAsync<T>(string url)
    {
        HttpResponseMessage resp;
        try { resp = await http.GetAsync(url); }
        catch (HttpRequestException) { throw new ApiException("Can't reach the DateNight server. Is it running?"); }
        return await ReadAsync<T>(resp);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType());
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req); }
        catch (HttpRequestException) { throw new ApiException("Can't reach the DateNight server. Is it running?"); }
        return await ReadAsync<T>(resp);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
        {
            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
            return (await resp.Content.ReadFromJsonAsync<T>())!;
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new ApiException("Please sign in first.");

        // Problem details ({ "detail": "..." }) or a plain JSON string.
        var text = await resp.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.String) throw new ApiException(doc.RootElement.GetString()!);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.GetString() is { } detail) throw new ApiException(detail);
            if (doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { } title) throw new ApiException(title);
        }
        catch (JsonException) { }

        throw new ApiException(string.IsNullOrWhiteSpace(text)
            ? $"Request failed ({(int)resp.StatusCode})."
            : text.Trim('"'));
    }
}

public static class Format
{
    public static string Price(decimal? min, decimal? max, bool isTicketed)
    {
        if (min is null && max is null) return isTicketed ? "Ticketed" : "Free / no ticket";
        var c = CultureInfo.GetCultureInfo("en-US");
        if (min is not null && max is not null && max > min) return $"{min.Value.ToString("C0", c)}–{max.Value.ToString("C0", c)}";
        return (min ?? max)!.Value.ToString("C0", c);
    }

    public static string When(DateTime? dt) =>
        dt is null ? "Any time" : dt.Value.ToString(dt.Value.TimeOfDay == TimeSpan.Zero ? "ddd, MMM d" : "ddd, MMM d · h:mm tt", CultureInfo.GetCultureInfo("en-US"));

    public static string? MapLink(double? lat, double? lon, string? fallback) =>
        lat is not null && lon is not null
            ? $"https://www.google.com/maps/search/?api=1&query={lat.Value.ToString(CultureInfo.InvariantCulture)},{lon.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.IsNullOrWhiteSpace(fallback) ? null : $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(fallback)}";
}
