using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileService _service;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private bool isStudent;
    [ObservableProperty] private bool gdprAccepted;
    [ObservableProperty] private bool liabilityAccepted;
    [ObservableProperty] private string? statusMessage;

    public ProfileViewModel(IProfileService service) => _service = service;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var p = await _service.GetAsync();
        Name = p.Name;
        Email = p.Email;
        IsStudent = p.IsStudent;
        GdprAccepted = p.GdprAccepted;
        LiabilityAccepted = p.LiabilityAccepted;
        StatusMessage = null;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        await _service.SaveAsync(new Profile
        {
            Name = Name,
            Email = Email,
            IsStudent = IsStudent,
            GdprAccepted = GdprAccepted,
            LiabilityAccepted = LiabilityAccepted
        });
        StatusMessage = "Profile saved.";
    }
}