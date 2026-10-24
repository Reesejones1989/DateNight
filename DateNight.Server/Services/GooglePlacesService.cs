using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DateNight.Shared;
using Microsoft.Extensions.Caching.Memory;

namespace DateNight.Server.Services;

/// <summary>
/// Finds restaurants and parks with Google Places API (New) "Nearby Search".
/// Docs: https://developers.google.com/maps/documentation/places/web-service/nearby-search
/// </summary>
public partial class GooglePlacesService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<GooglePlacesService> log)
{
    /// <summary>Categories this service handles; anything else stays on OpenStreetMap.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> CategoryTypes = new Dictionary<string, string[]>
    {
        ["food"] = ["restaurant", "fine_dining_restaurant", "brunch_restaurant", "cafe", "dessert_shop", "ice_cream_shop", "bakery"],
        ["outdoors"] = ["park", "national_park", "state_park", "hiking_area", "botanical_garden", "garden", "beach", "scenic_spot", "observation_deck", "picnic_ground"],
    };

    // Only ask for what we show: Google bills by the fields requested.
    private const string FieldMask =
        "places.id,places.displayName,places.shortFormattedAddress,places.formattedAddress,places.location," +
        "places.primaryType,places.primaryTypeDisplayName,places.rating,places.userRatingCount,places.priceLevel," +
        "places.websiteUri,places.googleMapsUri,places.reservable,places.photos,places.businessStatus";

    private string? ApiKey => config["GooglePlaces:ApiKey"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public bool Handles(string category) => CategoryTypes.ContainsKey(category);

    public async Task<(List<EventResult> Results, string? Warning)> SearchAsync(
        GeoPoint center, int radiusMiles, string category, DateTime? when, CancellationToken ct)
    {
        var types = CategoryTypes[category];

        // Nearby Search allows a radius up to 50 km (about 31 miles).
        var miles = Math.Min(radiusMiles, 31);
        string? warning = miles < radiusMiles ? "Google searches up to 31 miles away, so results are limited to that distance." : null;

        var body = new
        {
            includedTypes = types,
            maxResultCount = 20,
            rankPreference = "POPULARITY",
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude = center.Latitude, longitude = center.Longitude },
                    radius = Math.Round(miles * 1609.34, 0)
                }
            }
        };

        var cacheKey = $"gp:{category}:{center.Latitude:F4},{center.Longitude:F4}:{miles}";
        if (!cache.TryGetValue(cacheKey, out List<EventResult>? results))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchNearby")
            {
                Content = JsonContent.Create(body)
            };
            req.Headers.Add("X-Goog-Api-Key", ApiKey);
            req.Headers.Add("X-Goog-FieldMask", FieldMask);

            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Google Places returned {Status}: {Body}", resp.StatusCode, text);
                throw new HttpRequestException(ErrorMessage(resp.StatusCode, text));
            }

            results = Parse(text, center, category == "food");
            cache.Set(cacheKey, results, TimeSpan.FromHours(1));
        }

        // Reservation links depend on the date being planned, so add them after caching.
        if (category == "food")
            results = results!.Select(r => r with { ReserveUrl = OpenTableLink(r.Title, r.City ?? center.Name.Split(',')[0], when) }).ToList();

        return (results!, warning);
    }

    internal static List<EventResult> Parse(string json, GeoPoint center, bool isFood)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<EventResult>();
        if (!doc.RootElement.TryGetProperty("places", out var places)) return list;

        foreach (var p in places.EnumerateArray())
        {
            if (Str(p, "businessStatus") is { } status && status != "OPERATIONAL") continue;

            var name = p.TryGetProperty("displayName", out var dn) ? Str(dn, "text") : null;
            var id = Str(p, "id");
            if (name is null || id is null) continue;

            double? lat = null, lon = null;
            if (p.TryGetProperty("location", out var loc))
            {
                lat = loc.GetProperty("latitude").GetDouble();
                lon = loc.GetProperty("longitude").GetDouble();
            }

            string? photo = null;
            if (p.TryGetProperty("photos", out var photos) && photos.GetArrayLength() > 0)
                photo = Str(photos[0], "name");

            var typeName = p.TryGetProperty("primaryTypeDisplayName", out var pt) ? Str(pt, "text") : null;
            var priceLevel = PriceLevelText(Str(p, "priceLevel"));
            var full = Str(p, "formattedAddress");

            list.Add(new EventResult
            {
                ExternalId = id,
                Source = ItemSource.GooglePlaces,
                Title = name,
                Category = typeName ?? Pretty(Str(p, "primaryType")),
                Address = Str(p, "shortFormattedAddress") ?? full,
                City = CityFrom(full),
                Latitude = lat,
                Longitude = lon,
                DistanceMiles = lat is null ? null : Math.Round(GeoUtils.DistanceMiles(center.Latitude, center.Longitude, lat.Value, lon!.Value), 1),
                IsTicketed = false,
                Rating = p.TryGetProperty("rating", out var r) ? r.GetDouble() : null,
                RatingCount = p.TryGetProperty("userRatingCount", out var rc) ? rc.GetInt32() : null,
                PriceLevel = isFood ? priceLevel : (priceLevel == "Free" ? "Free" : null),
                Url = Str(p, "websiteUri") ?? Str(p, "googleMapsUri"),
                ImageUrl = photo is null ? null : $"api/photos?name={Uri.EscapeDataString(photo)}",
            });
        }
        return list;
    }

    /// <summary>
    /// Returns a short-lived image URL for a Google photo. Done on the server so the API key never reaches the browser.
    /// </summary>
    public async Task<string?> GetPhotoUrlAsync(string photoName, CancellationToken ct)
    {
        if (!IsConfigured || !PhotoNamePattern().IsMatch(photoName)) return null;
        var key = "gphoto:" + photoName;
        if (cache.TryGetValue(key, out string? cached)) return cached;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://places.googleapis.com/v1/{photoName}/media?maxWidthPx=640&skipHttpRedirect=true");
        req.Headers.Add("X-Goog-Api-Key", ApiKey);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var uri = Str(doc.RootElement, "photoUri");
        if (uri != null) cache.Set(key, uri, TimeSpan.FromHours(6));
        return uri;
    }

    [GeneratedRegex(@"^places/[A-Za-z0-9_-]+/photos/[A-Za-z0-9_-]+$")]
    private static partial Regex PhotoNamePattern();

    /// <summary>OpenTable search for this restaurant, prefilled for 2 people at the planned time.</summary>
    public static string OpenTableLink(string restaurant, string? city, DateTime? when)
    {
        var term = string.IsNullOrWhiteSpace(city) ? restaurant : $"{restaurant} {city}";
        var url = $"https://www.opentable.com/s?covers=2&term={Uri.EscapeDataString(term)}";
        if (when is { } w && w.TimeOfDay != TimeSpan.Zero)
            url += $"&dateTime={Uri.EscapeDataString(w.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture))}";
        return url;
    }

    private static string? PriceLevelText(string? level) => level switch
    {
        "PRICE_LEVEL_FREE" => "Free",
        "PRICE_LEVEL_INEXPENSIVE" => "$",
        "PRICE_LEVEL_MODERATE" => "$$",
        "PRICE_LEVEL_EXPENSIVE" => "$$$",
        "PRICE_LEVEL_VERY_EXPENSIVE" => "$$$$",
        _ => null
    };

    // "123 Main St, Atlanta, GA 30303, USA" -> "Atlanta"
    private static string? CityFrom(string? formatted)
    {
        if (formatted is null) return null;
        var parts = formatted.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? parts[^3] : null;
    }

    private static string ErrorMessage(System.Net.HttpStatusCode status, string body)
    {
        if (body.Contains("API_KEY_INVALID") || body.Contains("API key not valid"))
            return "Google rejected the API key. Check GooglePlaces:ApiKey.";
        if (body.Contains("SERVICE_DISABLED") || body.Contains("has not been used"))
            return "Places API (New) isn't enabled for this Google Cloud project. Enable it in the Google Cloud console.";
        if (body.Contains("BILLING"))
            return "Google Places needs billing turned on for your Google Cloud project.";
        return $"Google Places returned {(int)status}. Try again in a minute.";
    }

    private static string Pretty(string? s) =>
        string.IsNullOrEmpty(s) ? "Place" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
