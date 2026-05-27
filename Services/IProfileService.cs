using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public interface IProfileService
{
    Task<Profile> GetAsync();
    Task SaveAsync(Profile profile);
}