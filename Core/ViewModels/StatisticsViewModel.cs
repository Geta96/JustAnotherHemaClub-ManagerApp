using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public class LeaderboardRow
{
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public string RankText => $"#{Rank}";
    public string CountText { get; init; } = "";
}

public partial class StatisticsViewModel : ObservableObject
{
    private readonly IGoogleSheetsService _sheets;
    private readonly AuthService _auth;

    // Cancels any in-flight LoadAsync when the user navigates away mid-refresh.
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<MonthStatRow> Months { get; } = new();

    // Leaderboards
    public ObservableCollection<LeaderboardRow> TopAttendanceRecent { get; } = new();
    public ObservableCollection<LeaderboardRow> TopAttendanceAllTime { get; } = new();
    public ObservableCollection<LeaderboardRow> TopOneOnOneGivers { get; } = new();
    public ObservableCollection<LeaderboardRow> TopOneOnOneReceivers { get; } = new();

    // Club-wide compliance
    public ObservableCollection<Fencer> MissingGdpr { get; } = new();
    public ObservableCollection<Fencer> MissingLiability { get; } = new();

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

    public string RecentAttendanceSubtitle { get; private set; } = "";

    [ObservableProperty] private bool isLoading;

    public StatisticsViewModel(IGoogleSheetsService sheets, AuthService auth)
    {
        _sheets = sheets;
        _auth = auth;
    }

    /// <summary>
    /// Abandons any in-flight <see cref="LoadAsync"/>. Called when the page is
    /// disappearing so a manual refresh doesn't mutate the UI-bound collections
    /// after the CollectionView has been detached (crashes on Android when the
    /// RecyclerView has already been torn down).
    /// </summary>
    public void CancelLoad() => _loadCts?.Cancel();

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        // Cancel a previous in-flight load and start a fresh token for this one.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (showSpinner) IsLoading = true;
        try
        {
            // Only the data the Statistics page actually needs now.
            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var lessonsTask   = _sheets.GetIndividualLessonsAsync();
            await Task.WhenAll(fencersTask, trainingsTask, lessonsTask);

            ct.ThrowIfCancellationRequested();

            var fencers   = fencersTask.Result;
            var trainings = trainingsTask.Result;
            var lessons   = lessonsTask.Result;

            // Compliance snapshot.
            var missingGdpr      = fencers.Where(f => f.Active && !f.GdprAccepted).ToList();
            var missingLiability = fencers.Where(f => f.Active && !f.LiabilityAccepted).ToList();

            MissingGdpr.Clear();
            foreach (var f in missingGdpr) MissingGdpr.Add(f);
            MissingLiability.Clear();
            foreach (var f in missingLiability) MissingLiability.Add(f);

            OnPropertyChanged(nameof(AllGdprOk));
            OnPropertyChanged(nameof(AllLiabilityOk));
            OnPropertyChanged(nameof(GdprSummary));
            OnPropertyChanged(nameof(LiabilitySummary));
            OnPropertyChanged(nameof(IsLoggedInInstructor));

            // Grouping over all-time attendance / lessons is CPU work — build the
            // leaderboard rows off the UI thread, then publish on the dispatcher.
            var boards = await Task.Run(() => ComputeLeaderboards(fencers, trainings, lessons), ct);

            // The page may have been navigated away from while we computed.
            ct.ThrowIfCancellationRequested();

            RecentAttendanceSubtitle = boards.RecentSubtitle;
            OnPropertyChanged(nameof(RecentAttendanceSubtitle));

            Replace(TopAttendanceRecent, boards.Recent);
            Replace(TopAttendanceAllTime, boards.AllTime);
            Replace(TopOneOnOneGivers, boards.Givers);
            Replace(TopOneOnOneReceivers, boards.Receivers);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user navigates away mid-refresh — abandon quietly.
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    private sealed class LeaderboardData
    {
        public string RecentSubtitle = "";
        public List<LeaderboardRow> Recent = new();
        public List<LeaderboardRow> AllTime = new();
        public List<LeaderboardRow> Givers = new();
        public List<LeaderboardRow> Receivers = new();
    }

    private static LeaderboardData ComputeLeaderboards(
        List<Fencer> fencers,
        List<TrainingSession> trainings,
        List<IndividualLesson> lessons)
    {
        var nameById = fencers.ToDictionary(f => f.Id, f => f.Name);
        var instructorIds = fencers.Where(f => f.IsInstructor).Select(f => f.Id).ToHashSet();

        var today = DateTime.Today;
        var cutoff = new DateTime(today.Year, today.Month, 1).AddMonths(-2);

        var recentCounts = trainings
            .Where(t => t.Date.Date >= cutoff)
            .SelectMany(t => t.AttendeeFencerIds)
            .Where(id => !instructorIds.Contains(id) && nameById.ContainsKey(id))
            .GroupBy(id => id)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        var allTimeCounts = trainings
            .SelectMany(t => t.AttendeeFencerIds)
            .Where(id => !instructorIds.Contains(id) && nameById.ContainsKey(id))
            .GroupBy(id => id)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        var acceptedLessons = lessons
            .Where(l => l.Status == IndividualLessonStatus.Accepted)
            .ToList();

        var giverCounts = acceptedLessons
            .Where(l => !string.IsNullOrEmpty(l.InstructorId) &&
                        instructorIds.Contains(l.InstructorId) &&
                        nameById.ContainsKey(l.InstructorId))
            .GroupBy(l => l.InstructorId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        var receiverCounts = acceptedLessons
            .Where(l => !string.IsNullOrEmpty(l.StudentId) && nameById.ContainsKey(l.StudentId))
            .GroupBy(l => l.StudentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        return new LeaderboardData
        {
            RecentSubtitle = $"From {cutoff:MMM yyyy} to {today:MMM yyyy}",
            Recent = recentCounts.Select((x, i) => new LeaderboardRow
            {
                Rank = i + 1, Name = nameById[x.Id], Count = x.Count,
                CountText = $"{x.Count} session(s)"
            }).ToList(),
            AllTime = allTimeCounts.Select((x, i) => new LeaderboardRow
            {
                Rank = i + 1, Name = nameById[x.Id], Count = x.Count,
                CountText = $"{x.Count} session(s)"
            }).ToList(),
            Givers = giverCounts.Select((x, i) => new LeaderboardRow
            {
                Rank = i + 1, Name = nameById[x.Id], Count = x.Count,
                CountText = $"{x.Count} lesson(s)"
            }).ToList(),
            Receivers = receiverCounts.Select((x, i) => new LeaderboardRow
            {
                Rank = i + 1, Name = nameById[x.Id], Count = x.Count,
                CountText = $"{x.Count} lesson(s)"
            }).ToList()
        };
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}