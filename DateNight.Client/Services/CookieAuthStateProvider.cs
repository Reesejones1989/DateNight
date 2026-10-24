using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DateNight.Shared;
using Microsoft.AspNetCore.Components.Authorization;

namespace DateNight.Services;

/// <summary>Tracks whether the user is signed in, using the server's Identity cookie.</summary>
public class CookieAuthStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var resp = await http.GetAsync("account/me");
            if (!resp.IsSuccessStatusCode) return Anonymous;
            var me = await resp.Content.ReadFromJsonAsync<UserInfo>();
            if (me is null) return Anonymous;

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, me.Email),
                new Claim(ClaimTypes.Email, me.Email),
            }, authenticationType: "Cookie");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (HttpRequestException)
        {
            return Anonymous;
        }
    }

    private void Refresh()
    {
        _state = LoadAsync();
        NotifyAuthenticationStateChanged(_state);
    }

    /// <returns>null on success, otherwise an error message.</returns>
    public async Task<string?> LoginAsync(string email, string password)
    {
        var resp = await http.PostAsJsonAsync("account/login?useCookies=true", new { email, password });
        if (resp.IsSuccessStatusCode)
        {
            Refresh();
            return null;
        }
        return resp.StatusCode == HttpStatusCode.Unauthorized
            ? "That email and password don't match an account."
            : $"Sign-in failed ({(int)resp.StatusCode}).";
    }

    /// <returns>An empty list on success, otherwise the reasons registration failed.</returns>
    public async Task<List<string>> RegisterAsync(string email, string password)
    {
        var resp = await http.PostAsJsonAsync("account/register", new { email, password });
        if (resp.IsSuccessStatusCode) return new();

        var errors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("errors", out var errs))
                foreach (var field in errs.EnumerateObject())
                    foreach (var msg in field.Value.EnumerateArray())
                        errors.Add(msg.GetString() ?? "");
        }
        catch (JsonException) { }

        if (errors.Count == 0) errors.Add($"Registration failed ({(int)resp.StatusCode}).");
        return errors;
    }

    public async Task LogoutAsync()
    {
        await http.PostAsync("account/logout", null);
        Refresh();
    }
}
