using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public class HomeWeeklyRow
{
    public string DayShort { get; init; } = "";
    public string TimeRange { get; init; } = "";
    public string Topic { get; init; } = "";
    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);
}

public partial class HomeViewModel : ObservableObject
{
    public const string InstagramUrl =
        "https://www.instagram.com/just.another.hema.club?igsh=MXZudjI3MDJ4eWw1aA==";

    public const string FacebookUrl =
        "https://www.facebook.com/share/18VtVUQPW5/";

    public const string TelegramUrl =
        "https://t.me/+6EUfQu6kXPY4NWM8";

    private readonly IGoogleSheetsService _sheets;

    public ObservableCollection<HomeWeeklyRow> WeeklyTrainings { get; } = new();

    [ObservableProperty] private bool isLoadingWeekly;
    [ObservableProperty] private bool hasWeeklyTrainings;
    public bool HasNoWeeklyTrainings => !HasWeeklyTrainings && !IsLoadingWeekly;

    public HomeViewModel(IGoogleSheetsService sheets)
    {
        _sheets = sheets;
        WeeklyTrainings.CollectionChanged += (_, __) =>
        {
            HasWeeklyTrainings = WeeklyTrainings.Count > 0;
            OnPropertyChanged(nameof(HasNoWeeklyTrainings));
        };
    }

    partial void OnIsLoadingWeeklyChanged(bool value)
        => OnPropertyChanged(nameof(HasNoWeeklyTrainings));

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoadingWeekly = true;
        try
        {
            var rules = await _sheets.GetRecurringTrainingsAsync();

            // Active rules only, Monday-first ordering.
            var ordered = rules
                .Where(r => !r.EndDate.HasValue || r.EndDate.Value.Date >= DateTime.Today)
                .OrderBy(r => ((int)r.DayOfWeek + 6) % 7)
                .ThenBy(r => r.TimeOfDay);

            WeeklyTrainings.Clear();
            foreach (var r in ordered)
            {
                var end = r.EndTimeOfDay == default
                    ? r.TimeOfDay.Add(TimeSpan.FromMinutes(90))
                    : r.EndTimeOfDay;

                WeeklyTrainings.Add(new HomeWeeklyRow
                {
                    DayShort  = r.DayOfWeek.ToString().Substring(0, 3),
                    TimeRange = $"{r.TimeOfDay:hh\\:mm} – {end:hh\\:mm}",
                    Topic     = r.Topic ?? ""
                });
            }
        }
        catch
        {
            WeeklyTrainings.Clear();
        }
        finally { IsLoadingWeekly = false; }
    }

    [RelayCommand]
    private Task OpenInstagramAsync() => Launcher.Default.OpenAsync(InstagramUrl);

    [RelayCommand]
    private Task OpenFacebookAsync() => Launcher.Default.OpenAsync(FacebookUrl);

    [RelayCommand]
    private Task OpenTelegramAsync() => Launcher.Default.OpenAsync(TelegramUrl);
}