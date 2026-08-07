using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class PriceRuleRow : ObservableObject
{
    public PriceRule Rule { get; }

    [ObservableProperty] private int sessionCount;
    [ObservableProperty] private int monthCount;
    [ObservableProperty] private bool isCustomPeriod;
    [ObservableProperty] private decimal fullPrice;
    [ObservableProperty] private decimal studentPrice;
    [ObservableProperty] private DateTime startDate;
    [ObservableProperty] private bool hasEndDate;
    [ObservableProperty] private DateTime endDate;

    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private bool isExpanded;

    /// <summary>
    /// Set by <see cref="PricesViewModel.LoadAsync"/> on the last currently-active
    /// rule when at least one inactive rule follows it in the sorted list. The
    /// XAML draws a horizontal divider underneath that card so members can see
    /// at a glance where the "active today" group ends.
    /// </summary>
    [ObservableProperty] private bool showActiveSeparator;

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    // Suppresses auto-student-price + IsDirty side effects while the row is
    // being initialised from the underlying PriceRule.
    private bool _initializing;

    public bool IsActiveNow =>
        StartDate.Date <= DateTime.Today &&
        (!HasEndDate || EndDate.Date >= DateTime.Today);

    public string TierName => (SessionCount, MonthCount) switch
    {
        _ when IsCustomPeriod => "Custom period pass",
        (0, 2) => "2-month unlimited pass",
        (0, _) => "Unlimited monthly pass",
        (1, _) => "Single-session ticket",
        _      => $"{SessionCount}-session pass"
    };

    public string DateRange => HasEndDate
        ? $"{StartDate:yyyy-MM-dd} → {EndDate:yyyy-MM-dd}"
        : $"from {StartDate:yyyy-MM-dd}";

    public string PriceSummary => $"{FullPrice:N0} Ft · students {StudentPrice:N0} Ft";

    public string ActiveBadge => IsActiveNow ? "● Active" : "○ Inactive";

    public PriceRuleRow(PriceRule r)
    {
        _initializing = true;
        try
        {
            Rule         = r;
            SessionCount = r.SessionCount;
            MonthCount   = r.MonthCount < 1 ? 1 : r.MonthCount;
            IsCustomPeriod = r.IsCustomPeriod;
            FullPrice    = r.FullPrice;
            StudentPrice = r.StudentPrice;
            StartDate    = r.StartDate == default ? DateTime.Today : r.StartDate;
            HasEndDate   = r.EndDate.HasValue;
            EndDate      = r.EndDate ?? DateTime.Today.AddMonths(6);
            // Always start collapsed; expansion is a per-tap state from here on.
            IsExpanded   = false;
        }
        finally { _initializing = false; }
    }

    partial void OnSessionCountChanged(int value)
    {
        if (_initializing) return;
        IsDirty = true;
        OnPropertyChanged(nameof(TierName));
    }

    partial void OnMonthCountChanged(int value)
    {
        if (_initializing) return;
        IsDirty = true;
        OnPropertyChanged(nameof(TierName));
    }

    partial void OnFullPriceChanged(decimal value)
    {
        if (_initializing) return;
        StudentPrice = DuesCalculator.SuggestStudentPrice(value);
        IsDirty = true;
        OnPropertyChanged(nameof(PriceSummary));
    }

    partial void OnStudentPriceChanged(decimal value)
    {
        if (_initializing) return;
        IsDirty = true;
        OnPropertyChanged(nameof(PriceSummary));
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (_initializing) return;
        IsDirty = true;
        RaiseActiveAndRange();
    }

    partial void OnHasEndDateChanged(bool value)
    {
        if (_initializing) return;
        IsDirty = true;
        RaiseActiveAndRange();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (_initializing) return;
        IsDirty = true;
        RaiseActiveAndRange();
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public PriceRule ToUpdatedRule()
    {
        Rule.SessionCount = SessionCount;
        Rule.MonthCount   = MonthCount;
        Rule.IsCustomPeriod = IsCustomPeriod;
        Rule.FullPrice    = FullPrice;
        Rule.StudentPrice = StudentPrice;
        Rule.StartDate    = StartDate;
        Rule.EndDate      = HasEndDate ? EndDate : null;
        return Rule;
    }

    private void RaiseActiveAndRange()
    {
        OnPropertyChanged(nameof(IsActiveNow));
        OnPropertyChanged(nameof(DateRange));
        OnPropertyChanged(nameof(ActiveBadge));
    }
}

public partial class PricesViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;
    private readonly IDialogService _dialogs;

    public ObservableCollection<PriceRuleRow> Rules { get; } = new();

    // Session-count Picker options for the inline "Add price rule" form.
    // Index ↔ (SessionCount, MonthCount):
    //   0 → (1, 1)   single session
    //   1 → (4, 1)   4-session pass
    //   2 → (0, 1)   unlimited (1 month)
    //   3 → (0, 2)   unlimited (2 months)
    public IReadOnlyList<string> SessionCountOptions { get; } =
        new[] { "1 session", "4 sessions", "Unlimited (1 month)", "Custom period (unlimited)" };

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isAddingRule;

    [ObservableProperty] private int newSessionCountIndex;
    [ObservableProperty] private decimal newFullPrice;
    [ObservableProperty] private decimal newStudentPrice;
    [ObservableProperty] private DateTime newStartDate = DateTime.Today;
    [ObservableProperty] private bool newHasEndDate;
    [ObservableProperty] private DateTime newEndDate = DateTime.Today.AddMonths(6);

    public bool IsLoggedInInstructor => _auth.IsLoggedInInstructor;
    public bool HasNoRules => Rules.Count == 0;

    public PricesViewModel(IGoogleSheetsService sheets, AuthService auth, IDialogService dialogs)
    {
        _sheets = sheets;
        _auth = auth;
        _dialogs = dialogs;
        Rules.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasNoRules));
    }

    partial void OnNewFullPriceChanged(decimal value)
        => NewStudentPrice = DuesCalculator.SuggestStudentPrice(value);

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            var rules = await _sheets.GetPriceRulesAsync();

            // Active rules first, then by session count (Unlimited last), then by start date.
            var ordered = rules
                .OrderByDescending(r => r.IsActiveOn(DateTime.Today))
                .ThenBy(r => r.SessionCount == 0 ? int.MaxValue : r.SessionCount)
                .ThenBy(r => r.StartDate)
                .ToList();

            var rows = ordered.Select(r => new PriceRuleRow(r)).ToList();

            // Mark the boundary between "active today" and the rest so the
            // XAML can render a divider under the last active card. Only flag
            // it when there's actually an inactive card following — otherwise
            // the line would dangle at the bottom of the list.
            var lastActiveIdx = -1;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].IsActiveNow) lastActiveIdx = i;

            if (lastActiveIdx >= 0 && lastActiveIdx < rows.Count - 1)
                rows[lastActiveIdx].ShowActiveSeparator = true;

            Rules.Clear();
            foreach (var row in rows) Rules.Add(row);

            OnPropertyChanged(nameof(HasNoRules));
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    [RelayCommand] private void BeginAddRule() => IsAddingRule = true;

    [RelayCommand]
    private void CancelAddRule()
    {
        IsAddingRule = false;
        NewSessionCountIndex = 0;
        NewFullPrice = 0;
        NewStudentPrice = 0;
        NewStartDate = DateTime.Today;
        NewHasEndDate = false;
        NewEndDate = DateTime.Today.AddMonths(6);
    }

    [RelayCommand]
    private async Task SaveNewRuleAsync()
    {
        if (!IsLoggedInInstructor) return;
        if (NewFullPrice <= 0)
        {
            await ShowAsync("Cannot add price rule", "Please enter a full price greater than zero.");
            return;
        }

        var (sessions, months, isCustomPeriod) = NewSessionCountIndex switch
        {
            0 => (1, 1, false),
            1 => (4, 1, false),
            2 => (0, 1, false),
            _ => (0, 1, true)  // Custom period (unlimited)
        };

        // A custom-period pass is billed once for its whole window, so it needs
        // a concrete end date to know where the period stops.
        if (isCustomPeriod && !NewHasEndDate)
        {
            await ShowAsync("End date required",
                "A custom period pass needs an end date — tick \"Has end date\" and pick when the period ends.");
            return;
        }

        var rule = new PriceRule
        {
            SessionCount   = sessions,
            MonthCount     = months,
            IsCustomPeriod = isCustomPeriod,
            FullPrice      = NewFullPrice,
            StudentPrice = NewStudentPrice > 0
                ? NewStudentPrice
                : DuesCalculator.SuggestStudentPrice(NewFullPrice),
            StartDate = NewStartDate,
            EndDate   = NewHasEndDate ? NewEndDate : null
        };

        var conflict = FindOverlap(rule);
        if (conflict is not null && !await ConfirmOverlapAsync(rule, conflict))
            return;

        try
        {
            await _sheets.UpsertPriceRuleAsync(rule);
        }
        catch (Exception ex)
        {
            await ShowAsync("Could not save price rule",
                $"{ex.Message}\n\nMake sure a sheet tab named 'Prices' exists with the expected columns.");
            return;
        }

        CancelAddRule();
        await LoadAsync(showSpinner: false);
    }

    [RelayCommand]
    private async Task SaveRuleAsync(PriceRuleRow row)
    {
        if (row is null || !row.IsDirty || !IsLoggedInInstructor) return;

        // A custom-period pass must keep a concrete end date on edit too.
        if (row.IsCustomPeriod && !row.HasEndDate)
        {
            await ShowAsync("End date required",
                "A custom period pass needs an end date — tick \"Has end date\" and pick when the period ends.");
            return;
        }

        var updated = row.ToUpdatedRule();

        // Same overlap warning on edit. ignoreId excludes the row itself so
        // editing a rule never reports it as conflicting with its own state.
        var conflict = FindOverlap(updated, ignoreId: row.Rule.Id);
        if (conflict is not null && !await ConfirmOverlapAsync(updated, conflict))
            return;

        await _sheets.UpsertPriceRuleAsync(updated);
        row.IsDirty = false;
        row.IsExpanded = false;
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(PriceRuleRow row)
    {
        if (row is null || !IsLoggedInInstructor) return;

        var ok = await _dialogs.ConfirmAsync(
            "Delete price rule",
            $"Delete the {row.TierName.ToLowerInvariant()} ({row.PriceSummary})?",
            "Delete", "Cancel");
        if (!ok) return;

        await _sheets.DeletePriceRuleAsync(row.Rule.Id);
        Rules.Remove(row);
    }

    // ---------- Overlap detection ----------

    /// <summary>
    /// Returns the first already-loaded rule that shares <paramref name="candidate"/>'s
    /// SessionCount and whose date interval intersects it, or null when there is no
    /// conflict. EndDate=null is treated as "open-ended" (DateTime.MaxValue).
    /// </summary>
    private PriceRule? FindOverlap(PriceRule candidate, string? ignoreId = null) =>
        Rules
            .Select(r => r.Rule)
            .FirstOrDefault(existing =>
                (ignoreId is null || existing.Id != ignoreId) &&
                RulesOverlap(candidate, existing));

    private static bool RulesOverlap(PriceRule a, PriceRule b)
    {
        // Custom-period passes are a distinct tier from the standard unlimited
        // pass even though both use SessionCount 0 / MonthCount 1.
        if (a.IsCustomPeriod != b.IsCustomPeriod) return false;
        // Two rules are in the same tier only when both SessionCount AND MonthCount match.
        if (a.SessionCount != b.SessionCount) return false;
        if (a.MonthCount   != b.MonthCount)   return false;

        var aStart = a.StartDate.Date;
        var aEnd   = a.EndDate?.Date ?? DateTime.MaxValue.Date;
        var bStart = b.StartDate.Date;
        var bEnd   = b.EndDate?.Date ?? DateTime.MaxValue.Date;

        // Standard inclusive-interval overlap.
        return aStart <= bEnd && bStart <= aEnd;
    }

    private async Task<bool> ConfirmOverlapAsync(PriceRule candidate, PriceRule existing)
    {
        static string TierLabel(PriceRule r) => (r.SessionCount, r.MonthCount) switch
        {
            _ when r.IsCustomPeriod => "custom period pass",
            (0, 2) => "2-month unlimited pass",
            (0, _) => "Unlimited monthly pass",
            (1, _) => "Single-session ticket",
            _      => $"{r.SessionCount}-session pass"
        };

        static string Range(PriceRule r) => r.EndDate is { } e
            ? $"{r.StartDate:yyyy-MM-dd} → {e:yyyy-MM-dd}"
            : $"from {r.StartDate:yyyy-MM-dd} (no end date)";

        var tier = TierLabel(candidate).ToLowerInvariant();

        return await _dialogs.ConfirmAsync(
            "⚠ Overlaps with another rule",
            $"A {tier} already exists for {Range(existing)} at {existing.FullPrice:N0} Ft.\n\n" +
            $"This rule covers {Range(candidate)} at {candidate.FullPrice:N0} Ft and overlaps with it.\n\n" +
            "If you continue, this newer rule will be used whenever both apply.",
            "Save anyway", "Cancel");
    }

    private Task ShowAsync(string title, string message)
        => _dialogs.ShowAsync(title, message);
}