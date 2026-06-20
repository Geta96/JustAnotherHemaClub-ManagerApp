using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Selectors;

public class TrainingsHubTabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TrainingsTemplate { get; set; }
    public DataTemplate? WeeklyTemplate { get; set; }
    public DataTemplate? LessonsTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item switch
        {
            WeeklyViewModel _            => WeeklyTemplate!,
            IndividualLessonsViewModel _ => LessonsTemplate!,
            _                            => TrainingsTemplate!
        };
}