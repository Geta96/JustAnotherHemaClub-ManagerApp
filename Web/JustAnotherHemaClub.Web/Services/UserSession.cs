using System.Security.Claims;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Scoped holder for the signed-in fencer, mirroring how the MAUI app leans on
/// AuthService.CurrentFencer. Rehydrates AuthService from the auth cookie's
/// claims once per circuit, and raises a change event so the layout/nav can
/// react (e.g. show instructor-only menu items) after login/logout.
/// </summary>
public sealed class UserSession
{
    private readonly AuthService _auth;
    private readonly AuthenticationStateProvider _authState;
    private readonly TestDataService _testData;
    private bool _restored;

    public UserSession(AuthService auth, AuthenticationStateProvider authState, TestDataService testData)
    {
        _auth = auth;
        _authState = authState;
        _testData = testData;
    }

    public Fencer? CurrentFencer => _auth.CurrentFencer;
    public bool IsGuest => _auth.IsGuest;
    public bool IsInstructor => _auth.IsLoggedInInstructor;
    public bool IsLoggedIn => _auth.IsLoggedInFencer || _auth.IsGuest;

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Restores <see cref="AuthService.CurrentFencer"/> from the auth cookie's
    /// claims. Safe to call repeatedly; only the first call does work.
    /// </summary>
    public async Task EnsureRestoredAsync()
    {
        if (_restored) return;
        _restored = true;

        var state = await _authState.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true) return;

        var fencer = new Fencer
        {
            Id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = user.Identity.Name ?? "",
            Name = user.FindFirst("displayName")?.Value ?? "",
            Email = user.FindFirst("email")?.Value ?? "",
            IsInstructor = user.IsInRole("Instructor"),
            IsStudent = user.FindFirst("isStudent")?.Value == "1",
            Active = true,
        };

        _auth.RestoreSession(fencer);

        // Restore the in-memory test data swap if this session logged in as the
        // test user, so all Sheets calls stay routed to the dummy store.
        if (user.FindFirst("isTestMode")?.Value == "1")
            ServiceSwap.Activate(_testData);
    }
}