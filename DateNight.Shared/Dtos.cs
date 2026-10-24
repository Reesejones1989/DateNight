namespace DateNight.Shared;

/// <summary>Where an itinerary item came from.</summary>
public enum ItemSource
{
    Manual = 0,
    Ticketmaster = 1,
    OpenStreetMap = 2,
    GooglePlaces = 3
}

/// <summary>A search result: a ticketed event or a non-ticketed place.</summary>
public record EventResult
{
    public string ExternalId { get; init; } = "";
    public ItemSource Source { get; init; }
    public string Title { get; init; } = "";
    public string? Category { get; init; }
    public DateTime? StartTime { get; init; }
    public string? VenueName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceMiles { get; init; }
    public bool IsTicketed { get; init; }
    public decimal? PriceMin { get; init; }
    public decimal? PriceMax { get; init; }
    public string? Currency { get; init; }
    public string? Url { get; init; }
    public string? ImageUrl { get; init; }
    /// <summary>Google rating, 1.0–5.0.</summary>
    public double? Rating { get; init; }
    public int? RatingCount { get; init; }
    /// <summary>"$" to "$$$$", or "Free".</summary>
    public string? PriceLevel { get; init; }
    /// <summary>Link to book a table (OpenTable search for this restaurant).</summary>
    public string? ReserveUrl { get; init; }
}

public record SearchResponse(
    string LocationName,
    double Latitude,
    double Longitude,
    List<EventResult> Results,
    string? Warning = null,
    string? Provider = null);   // who supplied the results, for attribution: "Ticketmaster", "Google" or "OpenStreetMap"

public record ItinerarySummaryDto(
    int Id,
    string Title,
    DateOnly? Date,
    string? City,
    int ItemCount);

public record ItineraryDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public DateOnly? Date { get; init; }
    public string? City { get; init; }
    public string? Notes { get; init; }
    public List<ItineraryItemDto> Items { get; init; } = new();
}

public class ItineraryRequest
{
    public string Title { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Used both to read items and to create/update them.</summary>
public class ItineraryItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Location { get; set; }
    public string? Url { get; set; }
    public string? ReserveUrl { get; set; }
    public string? ImageUrl { get; set; }
    public ItemSource Source { get; set; }
    public string? ExternalId { get; set; }
    public bool IsTicketed { get; set; }
    public decimal? PriceMin { get; set; }
    public decimal? PriceMax { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public static ItineraryItemDto FromResult(EventResult r) => new()
    {
        Title = r.Title,
        Description = string.Join(" · ", new[] { r.Category, r.PriceLevel, r.Rating is { } rt ? $"★ {rt:0.0}" : null }.Where(x => !string.IsNullOrWhiteSpace(x))),
        StartTime = r.StartTime,
        Location = string.Join(", ", new[] { r.VenueName, r.Address, r.City }.Where(s => !string.IsNullOrWhiteSpace(s))),
        Url = r.Url,
        ReserveUrl = r.ReserveUrl,
        ImageUrl = r.ImageUrl,
        Source = r.Source,
        ExternalId = r.ExternalId,
        IsTicketed = r.IsTicketed,
        PriceMin = r.PriceMin,
        PriceMax = r.PriceMax,
        Latitude = r.Latitude,
        Longitude = r.Longitude
    };
}

public record UserInfo(string Email);
