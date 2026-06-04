using System.ComponentModel;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _vm;
        private const uint AnimMs = 350;

    // How far up the crest shifts when the sign-in card is visible.
    private const double CrestUpOffset = -140;

    // Distance the card/loading travel during their fade in/out.
    private const double CardTravel = 40;
    private const double LoadingTravel = 20;

    private bool _animating;
    private bool _stateDirty;

    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Initial state matches "form visible": crest up, card hidden offscreen-down, loading hidden.
        CrestGroup.TranslationY = CrestUpOffset;

        SignInCard.Opacity = 0;
        SignInCard.TranslationY = CardTravel;

        LoadingGroup.Opacity = 0;
        LoadingGroup.TranslationY = LoadingTravel;
        LoadingGroup.InputTransparent = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.InitializeAsync();
        await RunStateAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LoginViewModel.IsSilentLoggingIn))
            return;
        await RunStateAsync();
    }

    private async Task RunStateAsync()
    {
        if (_animating)
        {
            _stateDirty = true;
            return;
        }

        _animating = true;
        try
        {
            do
            {
                _stateDirty = false;
                await ApplyStateAsync(animate: true);
            }
            while (_stateDirty);
        }
        finally { _animating = false; }
    }

    private async Task ApplyStateAsync(bool animate)
    {
        var ms = animate ? AnimMs : 0u;

        // Cancel any animations still queued on these elements so the new ones run cleanly.
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(CrestGroup);
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(SignInCard);
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(LoadingGroup);

        if (_vm.IsSilentLoggingIn)
        {
            // Going to "logging in" state.
            SignInCard.InputTransparent = true;
            LoadingGroup.InputTransparent = false;

            // If loading was fully hidden, reset its start offset so it slides up.
            if (LoadingGroup.Opacity < 0.01) LoadingGroup.TranslationY = LoadingTravel;

            await Task.WhenAll(
                CrestGroup.TranslateTo(0, 0, ms, Easing.CubicInOut),
                SignInCard.TranslateTo(0, CardTravel, ms, Easing.CubicIn),
                SignInCard.FadeTo(0, ms, Easing.CubicIn),
                LoadingGroup.TranslateTo(0, 0, ms, Easing.CubicOut),
                LoadingGroup.FadeTo(1, ms, Easing.CubicOut));
        }
        else
        {
            // Going to "credentials" state.
            LoadingGroup.InputTransparent = true;
            SignInCard.InputTransparent = false;

            // If card was fully hidden, reset its start offset so it slides up.
            if (SignInCard.Opacity < 0.01) SignInCard.TranslationY = CardTravel;

            await Task.WhenAll(
                CrestGroup.TranslateTo(0, CrestUpOffset, ms, Easing.CubicInOut),
                SignInCard.TranslateTo(0, 0, ms, Easing.CubicOut),
                SignInCard.FadeTo(1, ms, Easing.CubicOut),
                LoadingGroup.TranslateTo(0, LoadingTravel, ms, Easing.CubicIn),
                LoadingGroup.FadeTo(0, ms, Easing.CubicIn));
        }
    }
}