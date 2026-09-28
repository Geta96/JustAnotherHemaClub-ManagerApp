namespace JustAnotherHemaClub.Services;

/// <summary>
/// MAUI implementation of <see cref="ICredentialProvider"/>. Reads the
/// service-account JSON that is bundled into the app package as a
/// <c>MauiAsset</c> (see the &lt;MauiAsset&gt; entry in the .csproj).
/// </summary>
public sealed class MauiCredentialProvider : ICredentialProvider
{
    private const string AssetName = "service-account.json";

    public Task<Stream> OpenServiceAccountAsync()
        => FileSystem.OpenAppPackageFileAsync(AssetName);
}
