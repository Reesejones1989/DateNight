using DateNight.Server.Services;
using DateNight.Shared;

namespace DateNight.Server.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/search");

        // GET /api/search/events?city=Atlanta&radius=25&from=2026-10-22&to=2026-10-29&keyword=jazz&category=music
        g.MapGet("/events", async (
            string city, int? radius, DateOnly? from, DateOnly? to, string? keyword, string? category,
            GeocodingService geo, TicketmasterService tm, CancellationToken ct) =>
        {
            if (!tm.IsConfigured)
                return Results.Problem(
                    "Ticketmaster API key isn't set. In the DateNight.Server folder run: dotnet user-secrets set \"Ticketmaster:ApiKey\" \"<your key>\"",
                    statusCode: 503);

            var start = from ?? DateOnly.FromDateTime(DateTime.Today);
            var end = to ?? start.AddDays(7);
            if (end < start) (start, end) = (end, start);

            try
            {
                var center = await geo.GeocodeAsync(city, ct);
                if (center is null) return Results.NotFound($"Couldn't find a place called \"{city}\".");

                var results = await tm.SearchAsync(center, radius ?? 25, start, end, keyword, category, ct);
                return Results.Ok(new SearchResponse(center.Name, center.Latitude, center.Longitude, results, Provider: "Ticketmaster"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return Results.Problem(ex is HttpRequestException ? ex.Message : "The event search timed out. Try again.", statusCode: 502);
            }
        });

        // GET /api/search/places?city=Atlanta&radius=10&category=food&date=2026-10-24
        // Restaurants and parks come from Google Places when a key is set; everything else (and the fallback) is OpenStreetMap.
        g.MapGet("/places", async (
            string city, int? radius, string? category, DateOnly? date,
            GeocodingService geo, GooglePlacesService google, PlacesService osm, CancellationToken ct) =>
        {
            category ??= "outdoors";
            try
            {
                var center = await geo.GeocodeAsync(city, ct);
                if (center is null) return Results.NotFound($"Couldn't find a place called \"{city}\".");

                if (google.Handles(category) && google.IsConfigured)
                {
                    var when = date?.ToDateTime(new TimeOnly(19, 0));
                    var (gResults, gWarning) = await google.SearchAsync(center, radius ?? 10, category, when, ct);
                    return Results.Ok(new SearchResponse(center.Name, center.Latitude, center.Longitude, gResults, gWarning, "Google"));
                }

                var (results, warning) = await osm.SearchAsync(center, radius ?? 10, category, ct);
                if (google.Handles(category))
                    warning = string.Join(" ", new[] { warning, "Add a Google Places API key for ratings, photos and better restaurant and park results." }.Where(w => w != null));
                return Results.Ok(new SearchResponse(center.Name, center.Latitude, center.Longitude, results, warning, "OpenStreetMap"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return Results.Problem(ex is HttpRequestException ? ex.Message : "The place search didn't respond in time. Try a smaller radius or try again.", statusCode: 502);
            }
        });

        // GET /api/photos?name=places/{id}/photos/{ref} -> redirects to the Google photo (keeps the API key on the server)
        app.MapGet("/api/photos", async (string name, GooglePlacesService google, CancellationToken ct) =>
        {
            var url = await google.GetPhotoUrlAsync(name, ct);
            return url is null ? Results.NotFound() : Results.Redirect(url);
        });
    }
}
