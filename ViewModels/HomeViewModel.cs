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
    private readonly ICacheControl _cache;
    private readonly AuthService _auth;

    public ObservableCollection<HomeWeeklyRow> WeeklyTrainings { get; } = new();

    [ObservableProperty] private bool isLoadingWeekly;
    [ObservableProperty] private bool hasWeeklyTrainings;
    public bool HasNoWeeklyTrainings => !HasWeeklyTrainings && !IsLoadingWeekly;

    // --- Next lesson card ---
    private TrainingSession? _nextLesson;

    [ObservableProperty] private bool hasNextLesson;
    [ObservableProperty] private string nextLessonWhen = "";
    [ObservableProperty] private string nextLessonTopic = "";
    [ObservableProperty] private bool isAttendingNextLesson;
    [ObservableProperty] private bool canAttendNextLesson;

    public bool NextLessonHasTopic => !string.IsNullOrWhiteSpace(NextLessonTopic);

    /// <summary>Show the blue Attend button only when the user can attend and hasn't yet.</summary>
    public bool ShowAttendButton => CanAttendNextLesson && !IsAttendingNextLesson;

    /// <summary>Show the Wine "Undo" text only when the user can attend and already is.</summary>
    public bool ShowUndoAttend => CanAttendNextLesson && IsAttendingNextLesson;

    // --- Attended visual state (green glow border on the card) ---
    private static readonly Color AttendedGreen = Color.FromArgb("#7FBF3F");
    private static readonly Color DefaultStroke = Color.FromArgb("#B9A9A0"); // matches the Muted card stroke

    public Color NextLessonStroke => IsAttendingNextLesson ? AttendedGreen : DefaultStroke;
    public double NextLessonStrokeThickness => IsAttendingNextLesson ? 3 : 1;
    public double NextLessonGlowOpacity => IsAttendingNextLesson ? 1 : 0;

    partial void OnNextLessonTopicChanged(string value)
        => OnPropertyChanged(nameof(NextLessonHasTopic));

    partial void OnIsAttendingNextLessonChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAttendButton));
        OnPropertyChanged(nameof(ShowUndoAttend));
        OnPropertyChanged(nameof(NextLessonStroke));
        OnPropertyChanged(nameof(NextLessonStrokeThickness));
        OnPropertyChanged(nameof(NextLessonGlowOpacity));
    }

    partial void OnCanAttendNextLessonChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAttendButton));
        OnPropertyChanged(nameof(ShowUndoAttend));
    }

    public HomeViewModel(IGoogleSheetsService sheets, ICacheControl cache, AuthService auth)
    {
        _sheets = sheets;
        _cache = cache;
        _auth = auth;
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

        // Load the "Next lesson" card (best-effort; a failure just hides it).
        await LoadNextLessonAsync();

        // Use the user's idle time on the home page to warm the datasets the
        // other tabs need (tournaments, individual lessons, recurring trainings,
        // …). Fire-and-forget: failures are swallowed inside PrefetchAsync's
        // callers and the pages fall back to their own network fetch. This makes
        // the first navigation to Tournaments / Trainings / Finance feel instant.
        _ = Task.Run(async () =>
        {
            try { await _cache.PrefetchAsync(); }
            catch { /* best-effort background warm-up */ }
        });
    }

    /// <summary>
    /// Finds the soonest upcoming training and populates the "Next lesson" card.
    /// A session stays "upcoming" until MIDNIGHT of its own day, so a fencer who
    /// opens the app after the session has ended can still attend it for the rest
    /// of that day. Only logged-in (non-guest) fencers see the Attend button.
    /// </summary>
    private async Task LoadNextLessonAsync()
    {
        try
        {
            var trainings = await _sheets.GetTrainingsAsync();

            // "Upcoming" = the session's day has not yet fully passed (visible
            // until midnight of the session date).
            var todayStart = DateTime.Today;
            var next = trainings
                .Where(t => t.Date.Date >= todayStart)
                .OrderBy(t => t.Date)
                .FirstOrDefault();

            _nextLesson = next;

            if (next is null)
            {
                HasNextLesson = false;
                CanAttendNextLesson = false;
                IsAttendingNextLesson = false;
                return;
            }

            NextLessonWhen  = $"{FormatFriendlyDay(next.Date)} at {next.Date:HH\\:mm}";
            NextLessonTopic = next.Topic ?? "";

            var me = _auth.CurrentFencer;
            CanAttendNextLesson   = me is not null && !_auth.IsGuest;
            IsAttendingNextLesson = me is not null && next.AttendeeFencerIds.Contains(me.Id);
            HasNextLesson = true;
        }
        catch
        {
            _nextLesson = null;
            HasNextLesson = false;
            CanAttendNextLesson = false;
        }
    }

    /// <summary>
    /// Human-friendly day label: "Today", "Tomorrow", "This Friday", "Next Tuesday",
    /// or an absolute "Oct 11th" for dates beyond next week.
    /// </summary>
    private static string FormatFriendlyDay(DateTime when)
    {
        var today = DateTime.Today;
        var date = when.Date;
        var days = (date - today).Days;

        if (days == 0) return "Today";
        if (days == 1) return "Tomorrow";

        // Within the next 7 days (2..7): "This <Weekday>".
        if (days >= 2 && days <= 7)
            return $"This {date.DayOfWeek}";

        // 8..14 days out: "Next <Weekday>".
        if (days >= 8 && days <= 14)
            return $"Next {date.DayOfWeek}";

        // Further out: absolute "MMM d" with an ordinal suffix, e.g. "Oct 11th".
        return $"{date:MMM} {date.Day}{OrdinalSuffix(date.Day)}";
    }

    private static string OrdinalSuffix(int day)
    {
        if (day is >= 11 and <= 13) return "th";
        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
    }

    /// <summary>
    /// Toggles the current fencer's attendance on the next lesson. Works both ways:
    /// attend if not yet attending, or undo (cancel) if already attending.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAttendNextLessonAsync()
    {
        var lesson = _nextLesson;
        var me = _auth.CurrentFencer;
        if (lesson is null || me is null || _auth.IsGuest) return;

        var wasAttending = lesson.AttendeeFencerIds.Contains(me.Id);

        // Optimistic local update.
        if (wasAttending) lesson.AttendeeFencerIds.Remove(me.Id);
        else if (!lesson.AttendeeFencerIds.Contains(me.Id)) lesson.AttendeeFencerIds.Add(me.Id);

        try
        {
            await _sheets.UpsertTrainingAsync(lesson);
            IsAttendingNextLesson = !wasAttending;
        }
        catch
        {
            // Roll back on failure so the UI stays consistent with the backend.
            if (wasAttending) lesson.AttendeeFencerIds.Add(me.Id);
            else lesson.AttendeeFencerIds.Remove(me.Id);
            IsAttendingNextLesson = wasAttending;
        }
    }

    [RelayCommand]
    private Task OpenInstagramAsync() => Launcher.Default.OpenAsync(InstagramUrl);

    [RelayCommand]
    private Task OpenFacebookAsync() => Launcher.Default.OpenAsync(FacebookUrl);

    [RelayCommand]
    private Task OpenTelegramAsync() => Launcher.Default.OpenAsync(TelegramUrl);
}