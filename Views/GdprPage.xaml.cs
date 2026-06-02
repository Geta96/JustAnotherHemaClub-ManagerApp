namespace JustAnotherHemaClub.Views;

public partial class GdprPage : ContentPage
{
    public GdprPage()
    {
        InitializeComponent();
    }

    private void OnEnglishClicked(object? sender, EventArgs e)
        => ShowLanguage(english: true);

    private void OnHungarianClicked(object? sender, EventArgs e)
        => ShowLanguage(english: false);

    private void ShowLanguage(bool english)
    {
        EnglishSection.IsVisible = english;
        HungarianSection.IsVisible = !english;

        var secondary = (Style?)Application.Current?.Resources["SecondaryButton"];

        EnglishButton.Style = english ? null : secondary;
        HungarianButton.Style = english ? secondary : null;
    }
}