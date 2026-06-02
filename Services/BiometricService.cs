using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace JustAnotherHemaClub.Services;

public sealed class BiometricService : IBiometricService
{
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var availability = await CrossFingerprint.Current.GetAvailabilityAsync(allowAlternativeAuthentication: false);
            return availability == FingerprintAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AuthenticateAsync(string reason)
    {
        try
        {
            var request = new AuthenticationRequestConfiguration(
                title: "JAHC Manager",
                reason: reason)
            {
                CancelTitle = "Cancel",
                AllowAlternativeAuthentication = true
            };
            var result = await CrossFingerprint.Current.AuthenticateAsync(request);
            return result.Authenticated;
        }
        catch
        {
            return false;
        }
    }
}