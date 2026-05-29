using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencersViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;

    // Data the picker uses
    public ObservableCollection<Fencer> Fencers { get; } = new();

    // Compliance (club-wide)
    public ObservableCollection<Fencer> MissingGdpr { get; } = new();
    public ObservableCollection<Fencer> MissingLiability { get; } = new();

    // Per-fencer attendance + payment status for the current month
    private Dictionary<string, (int Sessions, decimal Amount, bool Paid)> _statusByFencer = new();

    [ObservableProperty] private Fencer? selectedFencer;
    [ObservableProperty] private FencerDetailsVm? selectedDetails;

    public bool HasSelection => SelectedDetails is not null;

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

    public FencersViewModel(IGoogleSheetsService sheets) => _sheets = sheets;

    [RelayCommand]
    public async Task LoadAsync()
    {
        Fencers.Clear();
        MissingGdpr.Clear();
        MissingLiability.Clear();

        var all = (await _sheets.GetFencersAsync()).OrderBy(f => f.Name).ToList();
        foreach (var f in all) Fencers.Add(f);

        foreach (var f in all.Where(f => f.Active && !f.GdprAccepted))
            MissingGdpr.Add(f);

        foreach (var f in all.Where(f => f.Active && !f.LiabilityAccepted))
            MissingLiability.Add(f);

        // Current-month attendance + payments
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

        OnPropertyChanged(nameof(AllGdprOk));
        OnPropertyChanged(nameof(AllLiabilityOk));
        OnPropertyChanged(nameof(GdprSummary));
        OnPropertyChanged(nameof(LiabilitySummary));
    }

    partial void OnSelectedFencerChanged(Fencer? value) => RecomputeSelectedDetails();

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
}