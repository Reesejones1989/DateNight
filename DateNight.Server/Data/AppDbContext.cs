using DateNight.Shared;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DateNight.Server.Data;

public class AppUser : IdentityUser
{
    public List<Itinerary> Itineraries { get; set; } = new();
}

public class Itinerary
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public AppUser? Owner { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ItineraryItem> Items { get; set; } = new();
}

public class ItineraryItem
{
    public int Id { get; set; }
    public int ItineraryId { get; set; }
    public Itinerary? Itinerary { get; set; }
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
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<ItineraryItem> ItineraryItems => Set<ItineraryItem>();

    /// <summary>Sign-in cookie keys, stored here so people stay signed in after the server restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Itinerary>(e =>
        {
            e.Property(i => i.Title).HasMaxLength(200).IsRequired();
            e.HasOne(i => i.Owner).WithMany(u => u.Itineraries).HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => i.OwnerId);
        });

        b.Entity<ItineraryItem>(e =>
        {
            e.Property(i => i.Title).HasMaxLength(300).IsRequired();
            e.HasOne(i => i.Itinerary).WithMany(i => i.Items).HasForeignKey(i => i.ItineraryId).OnDelete(DeleteBehavior.Cascade);
            // SQLite has no native decimal type; store as double so ordering/comparison work.
            e.Property(i => i.PriceMin).HasConversion<double?>();
            e.Property(i => i.PriceMax).HasConversion<double?>();

            // Stop times are local "wall clock" times (7:00 PM wherever the date is), not UTC instants.
            if (Database.ProviderName == DatabaseSetup.NpgsqlProvider)
            {
                e.Property(i => i.StartTime).HasColumnType("timestamp without time zone");
                e.Property(i => i.EndTime).HasColumnType("timestamp without time zone");
            }
        });
    }
}

/// <summary>
/// Adds columns introduced after a database was first created, since EnsureCreated never changes an existing database.
/// </summary>
public static class SchemaUpgrades
{
    public static void Apply(AppDbContext db)
    {
        // Only SQLite databases made by older versions of the app need patching.
        // A new Postgres database is created with everything already in place.
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") return;

        AddColumnIfMissing(db, "ItineraryItems", "ReserveUrl", "TEXT NULL");
        Execute(db, """
            CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DataProtectionKeys" PRIMARY KEY AUTOINCREMENT,
                "FriendlyName" TEXT NULL,
                "Xml" TEXT NULL)
            """);
    }

    private static void Execute(AppDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(AppDbContext db, string table, string column, string definition)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            alter.ExecuteNonQuery();
        }
    }
}
