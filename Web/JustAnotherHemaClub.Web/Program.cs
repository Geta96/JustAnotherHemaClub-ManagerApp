using System.Security.Claims;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Web.Components;
using JustAnotherHemaClub.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering. The Google service-account
// JSON stays server-side (see ICredentialProvider): all Sheets I/O runs here,
// never in the browser.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The spreadsheet id and the service-account JSON path come from server config
// (appsettings.json / user-secrets / environment) instead of a bundled asset.
var spreadsheetId = builder.Configuration["GoogleSheets:SpreadsheetId"]
    ?? throw new InvalidOperationException("Missing config: GoogleSheets:SpreadsheetId");

// --- Core backends (same shape as MauiProgram) ---
builder.Services.AddSingleton<ICredentialProvider, WebCredentialProvider>();
builder.Services.AddSingleton(sp =>
    new GoogleSheetsService(spreadsheetId, sp.GetRequiredService<ICredentialProvider>()));
builder.Services.AddSingleton<CachedGoogleSheetsService>(sp =>
    new CachedGoogleSheetsService(sp.GetRequiredService<GoogleSheetsService>()));
builder.Services.AddSingleton<IGoogleSheetsService>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());
builder.Services.AddSingleton<ICacheControl>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());

// Test user in-memory data store (no network, no backend)
builder.Services.AddSingleton<TestDataService>();

// Auth + proxy. Scoped per circuit; rehydrated from the auth cookie each scope.
builder.Services.AddScoped<ICredentialStore, WebCredentialStore>();
builder.Services.AddScoped(sp =>
    new AuthService(sp, sp.GetRequiredService<ICredentialStore>()));

// Web dialog seam (JS-interop backed).
builder.Services.AddScoped<IDialogService, WebDialogService>();

// Shared session state for the signed-in user across components.
builder.Services.AddScoped<UserSession>();

// --- Cookie authentication ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.Name = "jahc.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

// --- Auth endpoints (non-interactive; the Login form POSTs here) ---
app.MapPost("/auth/login", async (
    HttpContext http,
    AuthService auth,
    TestDataService testData,
    [Microsoft.AspNetCore.Mvc.FromForm] string username,
    [Microsoft.AspNetCore.Mvc.FromForm] string password,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl) =>
{
    if (!await auth.LoginAsync(username, password) || auth.CurrentFencer is null)
        return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

    if (auth.IsTestMode)
        ServiceSwap.Activate(testData);

    var f = auth.CurrentFencer;
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, f.Id ?? ""),
        new(ClaimTypes.Name, f.Username ?? ""),
        new("displayName", f.Name ?? ""),
        new("email", f.Email ?? ""),
        new("isStudent", f.IsStudent ? "1" : "0"),
        new("isTestMode", auth.IsTestMode ? "1" : "0"),
    };
    if (f.IsInstructor)
        claims.Add(new Claim(ClaimTypes.Role, "Instructor"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    return Results.Redirect(target);
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http, AuthService auth) =>
{
    auth.Logout();
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();