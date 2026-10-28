using System.Security.Claims;
using DateNight.Server.Data;
using DateNight.Server.Endpoints;
using DateNight.Server.Services;
using DateNight.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Render (and most container hosts) tell the app which port to listen on through PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---- Database: SQLite file locally, Postgres (e.g. Neon) when ConnectionStrings:Default is a Postgres URL ----
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=datenight.db";
if (Environment.GetEnvironmentVariable("RENDER") is not null && !DatabaseSetup.IsPostgres(connectionString))
    throw new InvalidOperationException(
        "On Render, set the ConnectionStrings__Default environment variable to your Postgres URL. " +
        "Render's free disk is wiped on every restart, so a SQLite file would lose all data.");

builder.Services.AddDbContext<AppDbContext>(o => DatabaseSetup.Configure(o, connectionString));

// Keep the keys that protect sign-in cookies in the database, so restarts don't sign everyone out.
builder.Services.AddDataProtection()
    .SetApplicationName("DateNight")
    .PersistKeysToDbContext<AppDbContext>();

// ---- Accounts: ASP.NET Core Identity with cookie sign-in ----
builder.Services.AddAuthorization();
builder.Services.AddIdentityApiEndpoints<AppUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    // This is an API: answer 401/403 instead of redirecting to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

// ---- External data sources ----
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GeocodingService>(c => OsmHttp.Configure(c, builder.Configuration, TimeSpan.FromSeconds(10)));
builder.Services.AddHttpClient<TicketmasterService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<PlacesService>(c => OsmHttp.Configure(c, builder.Configuration, TimeSpan.FromSeconds(30)));
builder.Services.AddHttpClient<GooglePlacesService>(c => c.Timeout = TimeSpan.FromSeconds(15));

var app = builder.Build();

// Create the database on first run. (Switch to EF migrations once the schema settles.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrades.Apply(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();   // serves the Blazor WebAssembly client
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// Render checks this to know the app started.
app.MapGet("/healthz", () => Results.Ok("ok"));

// ---- Account endpoints: /account/register, /account/login?useCookies=true, etc. ----
var account = app.MapGroup("/account");
account.MapIdentityApi<AppUser>();
account.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Ok();
}).RequireAuthorization();
account.MapGet("/me", (ClaimsPrincipal user) =>
    user.Identity?.IsAuthenticated == true
        ? Results.Ok(new UserInfo(user.FindFirstValue(ClaimTypes.Email) ?? user.Identity.Name ?? ""))
        : Results.Unauthorized());

app.MapSearchEndpoints();
app.MapItineraryEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
