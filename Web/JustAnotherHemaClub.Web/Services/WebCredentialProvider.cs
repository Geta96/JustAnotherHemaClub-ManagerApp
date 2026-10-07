using System.Text;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Server-side source of the Google service-account JSON for the web app.
/// In local dev this reads a file (GoogleSheets:ServiceAccountJsonPath); in
/// hosting (e.g. Azure App Service) it can instead read the JSON directly from
/// GoogleSheets:ServiceAccountJson. The JSON is only ever read on the server —
/// it is never shipped to the browser.
/// </summary>
public sealed class WebCredentialProvider : ICredentialProvider
{
    private readonly string? _jsonPath;
    private readonly string? _jsonInline;

    public WebCredentialProvider(IConfiguration config, IWebHostEnvironment env)
    {
        // Prefer inline JSON (set as an Azure App Setting / environment variable).
        _jsonInline = config["GoogleSheets:ServiceAccountJson"];

        var configured = config["GoogleSheets:ServiceAccountJsonPath"];
        if (string.IsNullOrWhiteSpace(_jsonInline) && string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Missing config: set either GoogleSheets:ServiceAccountJson or GoogleSheets:ServiceAccountJsonPath.");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            // Allow either an absolute path or one relative to the content root.
            _jsonPath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, configured);
        }
    }

    public Task<Stream> OpenServiceAccountAsync()
    {
        if (!string.IsNullOrWhiteSpace(_jsonInline))
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(_jsonInline)));

        return Task.FromResult<Stream>(File.OpenRead(_jsonPath!));
    }
}