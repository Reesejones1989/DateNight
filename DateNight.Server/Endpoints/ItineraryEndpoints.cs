using System.Security.Claims;
using DateNight.Server.Data;
using DateNight.Shared;
using Microsoft.EntityFrameworkCore;

namespace DateNight.Server.Endpoints;

public static class ItineraryEndpoints
{
    public static void MapItineraryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/itineraries").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
            await db.Itineraries
                .Where(i => i.OwnerId == UserId(user))
                .OrderBy(i => i.Date == null).ThenBy(i => i.Date).ThenByDescending(i => i.CreatedUtc)
                .Select(i => new ItinerarySummaryDto(i.Id, i.Title, i.Date, i.City, i.Items.Count))
                .ToListAsync());

        g.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var it = await db.Itineraries.Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == UserId(user));
            return it is null ? Results.NotFound() : Results.Ok(ToDto(it));
        });

        g.MapPost("/", async (ItineraryRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest("Title is required.");
            var it = new Itinerary { OwnerId = UserId(user) };
            Apply(it, req);
            db.Itineraries.Add(it);
            await db.SaveChangesAsync();
            return Results.Created($"/api/itineraries/{it.Id}", ToDto(it));
        });

        g.MapPut("/{id:int}", async (int id, ItineraryRequest req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest("Title is required.");
            var it = await db.Itineraries.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id && i.OwnerId == UserId(user));
            if (it is null) return Results.NotFound();
            Apply(it, req);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(it));
        });

        g.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var n = await db.Itineraries.Where(i => i.Id == id && i.OwnerId == UserId(user)).ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });

        // ---- Items ----

        g.MapPost("/{id:int}/items", async (int id, ItineraryItemDto req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest("Title is required.");
            if (!await db.Itineraries.AnyAsync(i => i.Id == id && i.OwnerId == UserId(user))) return Results.NotFound();

            if (req.ExternalId != null &&
                await db.ItineraryItems.AnyAsync(x => x.ItineraryId == id && x.ExternalId == req.ExternalId && x.Source == req.Source))
                return Results.Conflict("That's already on this itinerary.");

            var item = new ItineraryItem { ItineraryId = id };
            Apply(item, req);
            db.ItineraryItems.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/itineraries/{id}/items/{item.Id}", ToDto(item));
        });

        g.MapPut("/{id:int}/items/{itemId:int}", async (int id, int itemId, ItineraryItemDto req, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest("Title is required.");
            var item = await db.ItineraryItems
                .FirstOrDefaultAsync(x => x.Id == itemId && x.ItineraryId == id && x.Itinerary!.OwnerId == UserId(user));
            if (item is null) return Results.NotFound();
            Apply(item, req);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(item));
        });

        g.MapDelete("/{id:int}/items/{itemId:int}", async (int id, int itemId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var n = await db.ItineraryItems
                .Where(x => x.Id == itemId && x.ItineraryId == id && x.Itinerary!.OwnerId == UserId(user))
                .ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    private static void Apply(Itinerary it, ItineraryRequest r)
    {
        it.Title = r.Title.Trim();
        it.Date = r.Date;
        it.City = r.City?.Trim();
        it.Notes = r.Notes;
    }

    private static void Apply(ItineraryItem i, ItineraryItemDto r)
    {
        i.Title = r.Title.Trim();
        i.Description = r.Description;
        i.StartTime = r.StartTime;
        i.EndTime = r.EndTime;
        i.Location = r.Location;
        i.Url = r.Url;
        i.ReserveUrl = r.ReserveUrl;
        i.ImageUrl = r.ImageUrl;
        i.Source = r.Source;
        i.ExternalId = r.ExternalId;
        i.IsTicketed = r.IsTicketed;
        i.PriceMin = r.PriceMin;
        i.PriceMax = r.PriceMax;
        i.Latitude = r.Latitude;
        i.Longitude = r.Longitude;
    }

    private static ItineraryDto ToDto(Itinerary it) => new()
    {
        Id = it.Id,
        Title = it.Title,
        Date = it.Date,
        City = it.City,
        Notes = it.Notes,
        Items = it.Items
            .OrderBy(i => i.StartTime == null).ThenBy(i => i.StartTime).ThenBy(i => i.CreatedUtc)
            .Select(ToDto).ToList()
    };

    private static ItineraryItemDto ToDto(ItineraryItem i) => new()
    {
        Id = i.Id,
        Title = i.Title,
        Description = i.Description,
        StartTime = i.StartTime,
        EndTime = i.EndTime,
        Location = i.Location,
        Url = i.Url,
        ReserveUrl = i.ReserveUrl,
        ImageUrl = i.ImageUrl,
        Source = i.Source,
        ExternalId = i.ExternalId,
        IsTicketed = i.IsTicketed,
        PriceMin = i.PriceMin,
        PriceMax = i.PriceMax,
        Latitude = i.Latitude,
        Longitude = i.Longitude
    };
}
