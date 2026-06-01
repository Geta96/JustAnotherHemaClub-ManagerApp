using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class FencersPage : ContentPage
{
    private readonly FencersViewModel _vm;

    public FencersPage(FencersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnPromoteClicked(object? sender, EventArgs e)
    {
        if (_vm.SelectedFencer is null) return;

        var confirm = await DisplayAlert(
            "Promote to instructor",
            $"Promote {_vm.SelectedFencer.Name} (@{_vm.SelectedFencer.Username}) to instructor?",
            "Promote",
            "Cancel");

        if (!confirm) return;

        var error = await _vm.PromoteSelectedAsync("", "");
        if (error is null)
        {
            await DisplayAlert("Done",
                $"{_vm.SelectedFencer.Name} is now an instructor.",
                "OK");
            await _vm.LoadAsync();
        }
        else
        {
            await DisplayAlert("Couldn't promote", error, "OK");
        }
    }
}