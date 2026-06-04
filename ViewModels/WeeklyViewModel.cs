using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class WeeklyRuleRow : ObservableObject
{
    public RecurringTrainingRule Rule { get; }

    [ObservableProperty] private string topic;
    [ObservableProperty] private TimeSpan timeOfDay;
    [ObservableProperty] private DateTime startDate;
    [ObservableProperty] private bool hasEndDate;
    [ObservableProperty] private DateTime endDate;
    [ObservableProperty] private bool isDirty;

    public string DayName => Rule.DayOfWeek.ToString();
    public string Summary =>
        $"Every {Rule.DayOfWeek} at {TimeOfDay:hh\\:mm}" +
        (HasEndDate ? $" until {EndDate:yyyy-MM-dd}" : "");

    public WeeklyRuleRow(RecurringTrainingRule r)
    {
        Rule = r;
        topic = r.Topic;
        timeOfDay = r.TimeOfDay;
        startDate = r.StartDate;
        hasEndDate = r.EndDate.HasValue;
        endDate = r.EndDate ?? DateTime.Today.AddMonths(1);
    }

    partial void OnTopicChanged(string value) { MarkDirty(); }
    partial void OnTimeOfDayChanged(TimeSpan value) { MarkDirty(); RaiseSummary(); }
    partial void OnStartDateChanged(DateTime value) { MarkDirty(); }
    partial void OnHasEndDateChanged(bool value) { MarkDirty(); RaiseSummary(); }
    partial void OnEndDateChanged(DateTime value) { MarkDirty(); RaiseSummary(); }

    private void MarkDirty() => IsDirty = true;
    private void RaiseSummary() => OnPropertyChanged(nameof(Summary));

    public RecurringTrainingRule ToUpdatedRule()
    {
        Rule.Topic = Topic ?? "";
        Rule.TimeOfDay = TimeOfDay;
        Rule.StartDate = StartDate;
        Rule.EndDate = HasEndDate ? EndDate : null;
        return Rule;
    }
}

public partial class WeeklyViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<WeeklyRuleRow> Rules { get; } = new();

    [ObservableProperty] private bool isLoading;

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;
    public bool IsNotLoggedInInstructor => !_auth.IsLoggedInInstructor;
    public bool HasNoRules => Rules.Count == 0;

    public WeeklyViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;

        // Keep HasNoRules accurate whenever the collection changes (add, remove, clear, etc.).
        Rules.CollectionChanged += (_, __) => NotifyHasNoRules();
    }

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            var rules = await _sheets.GetRecurringTrainingsAsync();

            var ordered = rules
                .OrderBy(r => ((int)r.DayOfWeek + 6) % 7)
                .ThenBy(r => r.TimeOfDay);

            Rules.Clear();
            foreach (var r in ordered) Rules.Add(new WeeklyRuleRow(r));

            NotifyHasNoRules(); // belt-and-braces; CollectionChanged handles it too
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveRuleAsync(WeeklyRuleRow row)
    {
        if (row is null || !row.IsDirty || !IsLoggedInInstructor) return;
        await _sheets.UpsertRecurringTrainingAsync(row.ToUpdatedRule());
        row.IsDirty = false;
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(WeeklyRuleRow row)
    {
        if (row is null || !IsLoggedInInstructor) return;

        var page = Application.Current?.MainPage;
        if (page is not null)
        {
            var ok = await page.DisplayAlert(
                "Delete recurring training",
                $"Stop the weekly {row.Rule.DayOfWeek} training at {row.TimeOfDay:hh\\:mm}?\n" +
                "Existing past sessions are kept; only future auto-creation stops.",
                "Delete", "Cancel");
            if (!ok) return;
        }

        await _sheets.DeleteRecurringTrainingAsync(row.Rule.Id);
        Rules.Remove(row);
    }

    // call from LoadAsync after populating Rules, and after Add/Remove:
    private void NotifyHasNoRules() => OnPropertyChanged(nameof(HasNoRules));
}