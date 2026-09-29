using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Server-side source of the Google service-account JSON for the web app.
/// The file path comes from configuration/secrets and the JSON is only ever
/// read on the server ? it is never shipped to the browser.
/// </summary>
public sealed class WebCredentialProvider : ICredentialProvider
{
    private readonly string _jsonPath;

    public WebCredentialProvider(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["GoogleSheets:ServiceAccountJsonPath"]
            ?? throw new InvalidOperationException("Missing config: GoogleSheets:ServiceAccountJsonPath");

        // Allow either an absolute path or one relative to the content root.
        _jsonPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    public Task<Stream> OpenServiceAccountAsync()
        => Task.FromResult<Stream>(File.OpenRead(_jsonPath));
}