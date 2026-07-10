// Minimal stub of AuthService for the test project.
// The real AuthService depends on MAUI SecureStorage/Preferences which don't
// exist in a unit test context. This stub exposes just enough surface for
// ViewModels that take AuthService as a constructor dependency.

using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class AuthService
{
    public Fencer? CurrentFencer { get; set; }
    public bool IsGuest { get; set; }

    public bool IsLoggedInInstructor =>
        CurrentFencer is not null && CurrentFencer.IsInstructor && !IsGuest;

    public bool IsLoggedInFencer =>
        CurrentFencer is not null && !IsGuest;

    // Constructor that matches what VMs expect (IServiceProvider not needed in tests).
    public AuthService() { }

    public static string Hash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
