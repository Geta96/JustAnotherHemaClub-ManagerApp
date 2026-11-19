using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public class NewsPage : ContentPage
{
    private readonly NewsViewModel _vm;
    private readonly NotificationSyncService _notificationSync;

    public NewsPage(NewsViewModel vm, NotificationSyncService notificationSync)
    {
        _vm = vm;
        _notificationSync = notificationSync;
        BindingContext = _vm;
        Title = "News";

        BuildUi();
    }

    private void BuildUi()
    {
        var wine = GetColor("Wine", Color.FromArgb("#5D1312"));
        var cream = GetColor("Cream", Color.FromArgb("#EEE7D5"));
        var danger = GetColor("DangerRed", Color.FromArgb("#B23A3A"));
        var slate = Color.FromArgb("#476FB5");
        var cardStyle = GetStyle("Card");
        var h1Style = GetStyle("H1");
        var h2Style = GetStyle("H2");
        var captionStyle = GetStyle("Caption");
        var refreshStyle = GetStyle("RefreshIcon");

        // Title + Refresh header
        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)),
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        var titleLabel = new Label { Text = "News", VerticalOptions = LayoutOptions.Center };
        if (h1Style is not null) titleLabel.Style = h1Style;
        headerGrid.Add(titleLabel, 0, 0);

        var refreshLabel = new Label();
        if (refreshStyle is not null) refreshLabel.Style = refreshStyle;
        else refreshLabel.Text = "↻";
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await _vm.LoadAsync();
        refreshLabel.GestureRecognizers.Add(tap);
        headerGrid.Add(refreshLabel, 1, 0);

        // Create news card (instructors only, collapsed by default)
        var createCard = new Border();
        if (cardStyle is not null) createCard.Style = cardStyle;
        createCard.SetBinding(IsVisibleProperty, nameof(NewsViewModel.IsInstructor));

        var createHeaderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)),
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        var createHeading = new Label { Text = "Create news", VerticalOptions = LayoutOptions.Center };
        if (h2Style is not null) createHeading.Style = h2Style;

        var createChevron = new Label
        {
            TextColor = wine,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        createChevron.SetBinding(Label.TextProperty, nameof(NewsViewModel.CreateChevronText));

        createHeaderRow.Add(createHeading, 0, 0);
        createHeaderRow.Add(createChevron, 1, 0);

        var createHeaderTap = new TapGestureRecognizer();
        createHeaderTap.SetBinding(TapGestureRecognizer.CommandProperty, nameof(NewsViewModel.ToggleCreateCommand));
        createHeaderRow.GestureRecognizers.Add(createHeaderTap);

        var titleCap = new Label { Text = "Title" };
        if (captionStyle is not null) titleCap.Style = captionStyle;

        var titleEntry = new Entry { Placeholder = "News title" };
        titleEntry.SetBinding(Entry.TextProperty, new Binding(nameof(NewsViewModel.NewTitle), BindingMode.TwoWay));

        var bodyCap = new Label { Text = "Body" };
        if (captionStyle is not null) bodyCap.Style = captionStyle;

        var bodyEditor = new Editor
        {
            Placeholder = "Write announcement...",
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 80
        };
        bodyEditor.SetBinding(Editor.TextProperty, new Binding(nameof(NewsViewModel.NewBody), BindingMode.TwoWay));

        var publishBtn = new Button
        {
            Text = "Publish",
            BackgroundColor = slate,
            TextColor = cream
        };
        publishBtn.SetBinding(Button.CommandProperty, nameof(NewsViewModel.CreateNewsCommand));

        var statusLbl = new Label();
        if (captionStyle is not null) statusLbl.Style = captionStyle;
        statusLbl.SetBinding(Label.TextProperty, nameof(NewsViewModel.StatusMessage));

        var createFormStack = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { titleCap, titleEntry, bodyCap, bodyEditor, publishBtn, statusLbl }
        };
        createFormStack.SetBinding(IsVisibleProperty, nameof(NewsViewModel.IsCreateExpanded));

        createCard.Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children = { createHeaderRow, createFormStack }
        };

        // Loading indicator
        var loadingIndicator = new ActivityIndicator
        {
            Color = wine,
            HorizontalOptions = LayoutOptions.Center
        };
        loadingIndicator.SetBinding(ActivityIndicator.IsRunningProperty, nameof(NewsViewModel.IsLoading));
        loadingIndicator.SetBinding(IsVisibleProperty, nameof(NewsViewModel.IsLoading));

        // Empty state card
        var emptyCard = new Border();
        if (cardStyle is not null) emptyCard.Style = cardStyle;
        emptyCard.SetBinding(IsVisibleProperty, nameof(NewsViewModel.HasNoNews));
        var emptyLbl = new Label { Text = "No news yet." };
        if (captionStyle is not null) emptyLbl.Style = captionStyle;
        emptyCard.Content = emptyLbl;

        // News cards list
        var newsStack = new VerticalStackLayout { Spacing = 12 };
        BindableLayout.SetItemsSource(newsStack, _vm.News);
        BindableLayout.SetItemTemplate(newsStack, new DataTemplate(() =>
        {
            var itemCard = new Border();
            if (cardStyle is not null) itemCard.Style = cardStyle;

            // --- Read-only view ---
            var topRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)),
                ColumnSpacing = 8
            };

            var itemTitle = new Label
            {
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                TextColor = wine,
                LineBreakMode = LineBreakMode.WordWrap
            };
            if (h2Style is not null) itemTitle.Style = h2Style;
            itemTitle.SetBinding(Label.TextProperty, nameof(NewsRow.Title));

            var itemDate = new Label { VerticalOptions = LayoutOptions.Start };
            if (captionStyle is not null) itemDate.Style = captionStyle;
            itemDate.SetBinding(Label.TextProperty, nameof(NewsRow.DateText));

            topRow.Add(itemTitle, 0, 0);
            topRow.Add(itemDate, 1, 0);

            var itemBody = new Label
            {
                LineHeight = 1.3,
                LineBreakMode = LineBreakMode.WordWrap
            };
            itemBody.SetBinding(Label.TextProperty, nameof(NewsRow.Body));

            var bottomRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)),
                ColumnSpacing = 8
            };

            var authorLbl = new Label { VerticalOptions = LayoutOptions.Center };
            if (captionStyle is not null) authorLbl.Style = captionStyle;
            authorLbl.SetBinding(Label.TextProperty, new Binding(nameof(NewsRow.AuthorName), stringFormat: "Posted by {0}"));

            var editBtn = new Button
            {
                Text = "Edit",
                BackgroundColor = slate,
                TextColor = cream,
                HeightRequest = 36,
                Padding = new Thickness(12, 0),
                IsVisible = _vm.IsInstructor
            };
            editBtn.Clicked += (s, _) =>
            {
                if (s is Button b && b.BindingContext is NewsRow r)
                    r.BeginEdit();
            };

            var deleteBtn = new Button
            {
                Text = "Delete",
                BackgroundColor = danger,
                TextColor = cream,
                HeightRequest = 36,
                Padding = new Thickness(12, 0),
                IsVisible = _vm.IsInstructor
            };
            deleteBtn.Clicked += async (s, _) =>
            {
                if (s is Button b && b.BindingContext is NewsRow r)
                    await _vm.DeleteNewsAsync(r);
            };

            bottomRow.Add(authorLbl, 0, 0);
            bottomRow.Add(editBtn, 1, 0);
            bottomRow.Add(deleteBtn, 2, 0);

            var readStack = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { topRow, itemBody, bottomRow }
            };
            readStack.SetBinding(IsVisibleProperty, nameof(NewsRow.IsNotEditing));

            // --- Inline edit view (instructors only) ---
            var editTitleCap = new Label { Text = "Title" };
            if (captionStyle is not null) editTitleCap.Style = captionStyle;

            var editTitleEntry = new Entry { Placeholder = "News title" };
            editTitleEntry.SetBinding(Entry.TextProperty, new Binding(nameof(NewsRow.EditTitle), BindingMode.TwoWay));

            var editBodyCap = new Label { Text = "Body" };
            if (captionStyle is not null) editBodyCap.Style = captionStyle;

            var editBodyEditor = new Editor
            {
                Placeholder = "Write announcement...",
                AutoSize = EditorAutoSizeOption.TextChanges,
                MinimumHeightRequest = 80
            };
            editBodyEditor.SetBinding(Editor.TextProperty, new Binding(nameof(NewsRow.EditBody), BindingMode.TwoWay));

            var editActions = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)),
                ColumnSpacing = 10
            };

            var cancelBtn = new Button
            {
                Text = "Cancel",
                BackgroundColor = wine,
                TextColor = cream
            };
            cancelBtn.Clicked += (s, _) =>
            {
                if (s is Button b && b.BindingContext is NewsRow r)
                    r.CancelEdit();
            };

            var saveBtn = new Button
            {
                Text = "Save",
                BackgroundColor = slate,
                TextColor = cream
            };
            saveBtn.Clicked += async (s, _) =>
            {
                if (s is Button b && b.BindingContext is NewsRow r)
                    await _vm.SaveNewsAsync(r);
            };

            editActions.Add(cancelBtn, 0, 0);
            editActions.Add(saveBtn, 1, 0);

            var editStack = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { editTitleCap, editTitleEntry, editBodyCap, editBodyEditor, editActions }
            };
            editStack.SetBinding(IsVisibleProperty, nameof(NewsRow.IsEditing));

            itemCard.Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { readStack, editStack }
            };
            return itemCard;
        }));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16),
                Spacing = 14,
                Children = { headerGrid, createCard, loadingIndicator, emptyCard, newsStack }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        _ = Task.Run(async () =>
        {
            try { await _notificationSync.CheckAndNotifyAsync(); }
            catch { }
        });
    }

    private static Color GetColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c
            ? c
            : fallback;

    private static Style? GetStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style s
            ? s
            : null;
}
