using System.Security.Claims;
using DateNight.Server.Data;
using DateNight.Server.Endpoints;
using DateNight.Server.Services;
using DateNight.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (SQLite file next to the server) ----
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=datenight.db"));

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
const string userAgent = "DateNightApp/1.0 (personal project)";
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GeocodingService>(c => { c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); c.Timeout = TimeSpan.FromSeconds(10); });
builder.Services.AddHttpClient<TicketmasterService>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<PlacesService>(c => { c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); c.Timeout = TimeSpan.FromSeconds(30); });
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
