using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencersViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    public ObservableCollection<Fencer> Fencers { get; } = new();
    public ObservableCollection<Fencer> MissingGdpr { get; } = new();
    public ObservableCollection<Fencer> MissingLiability { get; } = new();

    private Dictionary<string, (int Sessions, decimal Amount, bool Paid)> _statusByFencer = new();

    [ObservableProperty] private Fencer? selectedFencer;
    [ObservableProperty] private FencerDetailsVm? selectedDetails;

    // Visible diagnostics on the page
    [ObservableProperty] private bool backendRequestSucceeded;
    [ObservableProperty] private string backendStatus = "Not loaded yet.";
    [ObservableProperty] private string? backendError;
    [ObservableProperty] private bool isLoading;

    public bool HasSelection => SelectedDetails is not null;
    public bool CanPromoteSelected =>
        _auth.IsLoggedInInstructor &&
        SelectedFencer is not null &&
        !SelectedFencer.IsInstructor;

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;

    public bool AllGdprOk => MissingGdpr.Count == 0;
    public bool AllLiabilityOk => MissingLiability.Count == 0;

    public string GdprSummary =>
        AllGdprOk
            ? "All active fencers signed the GDPR statement."
            : $"{MissingGdpr.Count} fencer(s) missing GDPR consent:";

    public string LiabilitySummary =>
        AllLiabilityOk
            ? "All active fencers signed the liability statement."
            : $"{MissingLiability.Count} fencer(s) missing liability statement:";

    public FencersViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        BackendRequestSucceeded = false;
        BackendError = null;
        BackendStatus = "Loading fencers from Google Sheets...";

        Fencers.Clear();
        MissingGdpr.Clear();
        MissingLiability.Clear();

        try
        {
            var all = (await _sheets.GetFencersAsync()).OrderBy(f => f.Name).ToList();

            foreach (var f in all)
                Fencers.Add(f);

            foreach (var f in all.Where(f => f.Active && !f.GdprAccepted))
                MissingGdpr.Add(f);

            foreach (var f in all.Where(f => f.Active && !f.LiabilityAccepted))
                MissingLiability.Add(f);

            var today = DateTime.Today;
            var trainings = (await _sheets.GetTrainingsAsync())
                .Where(t => t.Date.Year == today.Year && t.Date.Month == today.Month)
                .ToList();

            var payments = await _sheets.GetPaymentsAsync(today.Year, today.Month);

            var attendance = trainings
                .SelectMany(t => t.AttendeeFencerIds)
                .GroupBy(id => id)
                .ToDictionary(g => g.Key, g => g.Count());

            _statusByFencer = all.ToDictionary(
                f => f.Id,
                f =>
                {
                    attendance.TryGetValue(f.Id, out var count);
                    var amount = DuesCalculator.Calculate(count, f.IsStudent);
                    var paid = payments.Any(p => p.FencerId == f.Id);
                    return (count, amount, paid);
                });

            SelectedFencer ??= Fencers.FirstOrDefault();
            RecomputeSelectedDetails();

            BackendRequestSucceeded = true;
            BackendStatus = $"Backend request successful. Loaded {Fencers.Count} fencer(s).";

            OnPropertyChanged(nameof(AllGdprOk));
            OnPropertyChanged(nameof(AllLiabilityOk));
            OnPropertyChanged(nameof(GdprSummary));
            OnPropertyChanged(nameof(LiabilitySummary));
            OnPropertyChanged(nameof(IsLoggedInInstructor));
            OnPropertyChanged(nameof(CanPromoteSelected));
        }
        catch (Exception ex)
        {
            BackendRequestSucceeded = false;
            BackendError = ex.ToString();
            BackendStatus = "Backend request failed.";
            SelectedDetails = null;
            OnPropertyChanged(nameof(HasSelection));
        }
        finally { IsLoading = false; }
    }

    partial void OnSelectedFencerChanged(Fencer? value)
    {
        RecomputeSelectedDetails();
        OnPropertyChanged(nameof(CanPromoteSelected));
    }

    private void RecomputeSelectedDetails()
    {
        if (SelectedFencer is null)
        {
            SelectedDetails = null;
        }
        else if (_statusByFencer.TryGetValue(SelectedFencer.Id, out var s))
        {
            SelectedDetails = new FencerDetailsVm(SelectedFencer, s.Sessions, s.Amount, s.Paid);
        }
        else
        {
            SelectedDetails = new FencerDetailsVm(SelectedFencer, 0, 0m, true);
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    public async Task<string?> PromoteSelectedAsync(string username, string password)
    {
        if (!_auth.IsLoggedInInstructor) return "Only logged-in instructors can promote.";
        if (SelectedFencer is null) return "Pick a fencer first.";
        if (SelectedFencer.IsInstructor) return "This fencer is already an instructor.";

        try
        {
            SelectedFencer.IsInstructor = true;
            await _sheets.UpsertFencerAsync(SelectedFencer);

            OnPropertyChanged(nameof(CanPromoteSelected));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}