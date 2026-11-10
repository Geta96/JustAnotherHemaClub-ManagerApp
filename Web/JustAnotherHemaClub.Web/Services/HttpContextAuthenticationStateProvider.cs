using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Supplies the Blazor authentication state from the current HttpContext's
/// cookie-authenticated principal. The initial (server-rendered) request carries
/// the cookie, so the interactive circuit inherits the signed-in identity.
/// Login/logout force a full reload, which refreshes this state.
/// </summary>
public sealed class HttpContextAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _http;

    public HttpContextAuthenticationStateProvider(IHttpContextAccessor http) => _http = http;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = _http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(principal));
    }
}
