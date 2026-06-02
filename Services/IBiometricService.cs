namespace JustAnotherHemaClub.Services;

public interface IBiometricService
{
    Task<bool> IsAvailableAsync();
    Task<bool> AuthenticateAsync(string reason);
}