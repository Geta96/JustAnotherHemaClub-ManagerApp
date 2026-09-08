namespace JustAnotherHemaClub.Services;

/// <summary>
/// Platform-agnostic source of the Google service-account credential JSON.
///
/// This is the seam that lets <see cref="GoogleSheetsService"/> run on both
/// MAUI (where the JSON is bundled as a packaged app asset) and on the Blazor
/// web server (where the JSON is supplied from server-side configuration/secrets
/// and must NEVER be shipped to the browser).
/// </summary>
public interface ICredentialProvider
{
    /// <summary>
    /// Opens a readable stream over the service-account JSON. The caller owns
    /// the returned stream and is responsible for disposing it.
    /// </summary>
    Task<Stream> OpenServiceAccountAsync();
}
