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

    [RelayCommand]
    public async Task LoadAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            // Only the data the Statistics page actually needs now.
            var fencersTask   = _sheets.GetFencersAsync();
            var trainingsTask = _sheets.GetTrainingsAsync();
            var lessonsTask   = _sheets.GetIndividualLessonsAsync();
            await Task.WhenAll(fencersTask, trainingsTask, lessonsTask);

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

            BuildLeaderboards(fencers, trainings, lessons);
        }
        finally { if (showSpinner) IsLoading = false; }
    }

    private void BuildLeaderboards(
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

        RecentAttendanceSubtitle = $"From {cutoff:MMM yyyy} to {today:MMM yyyy}";
        OnPropertyChanged(nameof(RecentAttendanceSubtitle));

        Replace(TopAttendanceRecent, recentCounts.Select((x, i) => new LeaderboardRow
        {
            Rank = i + 1,
            Name = nameById[x.Id],
            Count = x.Count,
            CountText = $"{x.Count} session(s)"
        }));

        var allTimeCounts = trainings
            .SelectMany(t => t.AttendeeFencerIds)
            .Where(id => !instructorIds.Contains(id) && nameById.ContainsKey(id))
            .GroupBy(id => id)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        Replace(TopAttendanceAllTime, allTimeCounts.Select((x, i) => new LeaderboardRow
        {
            Rank = i + 1,
            Name = nameById[x.Id],
            Count = x.Count,
            CountText = $"{x.Count} session(s)"
        }));

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

        Replace(TopOneOnOneGivers, giverCounts.Select((x, i) => new LeaderboardRow
        {
            Rank = i + 1,
            Name = nameById[x.Id],
            Count = x.Count,
            CountText = $"{x.Count} lesson(s)"
        }));

        var receiverCounts = acceptedLessons
            .Where(l => !string.IsNullOrEmpty(l.StudentId) && nameById.ContainsKey(l.StudentId))
            .GroupBy(l => l.StudentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        Replace(TopOneOnOneReceivers, receiverCounts.Select((x, i) => new LeaderboardRow
        {
            Rank = i + 1,
            Name = nameById[x.Id],
            Count = x.Count,
            CountText = $"{x.Count} lesson(s)"
        }));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}