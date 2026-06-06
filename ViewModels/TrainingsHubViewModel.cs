using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JustAnotherHemaClub.ViewModels;

public partial class TrainingsHubViewModel : ObservableObject
{
    public TrainingsViewModel TrainingsVm { get; }
    public WeeklyViewModel WeeklyVm { get; }
    public IndividualLessonsViewModel LessonsVm { get; }

    /// <summary>
    /// Carousel items ARE the sub-VMs, so each tab page starts with the right
    /// BindingContext without needing a RelativeSource walk to the ContentPage.
    /// </summary>
    public IReadOnlyList<object> Tabs { get; }

    [ObservableProperty] private int selectedTabIndex;
    public bool IsTrainingsTab => SelectedTabIndex == 0;
    public bool IsWeeklyTab    => SelectedTabIndex == 1;
    public bool IsLessonsTab   => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTrainingsTab));
        OnPropertyChanged(nameof(IsWeeklyTab));
        OnPropertyChanged(nameof(IsLessonsTab));
    }

    [RelayCommand] private void ShowTrainingsTab() => SelectedTabIndex = 0;
    [RelayCommand] private void ShowWeeklyTab()    => SelectedTabIndex = 1;
    [RelayCommand] private void ShowLessonsTab()   => SelectedTabIndex = 2;

    [ObservableProperty] private bool isLoading;

    public TrainingsHubViewModel(
        TrainingsViewModel trainingsVm,
        WeeklyViewModel weeklyVm,
        IndividualLessonsViewModel lessonsVm)
    {
        TrainingsVm = trainingsVm;
        WeeklyVm    = weeklyVm;
        LessonsVm   = lessonsVm;
        Tabs = new object[] { TrainingsVm, WeeklyVm, LessonsVm };
    }

    public async Task LoadAllAsync(bool showSpinner = false)
    {
        if (showSpinner) IsLoading = true;
        try
        {
            await Task.WhenAll(
                TrainingsVm.LoadAsync(false),
                WeeklyVm.LoadAsync(false),
                LessonsVm.LoadAsync(false));
        }
        finally { if (showSpinner) IsLoading = false; }
    }
}