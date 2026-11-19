using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Moq;

namespace JustAnotherHemaClub.Tests.ViewModels;

public class NewsViewModelTests
{
    private readonly Mock<IGoogleSheetsService> _sheets = new();
    private readonly Mock<ICacheControl> _cache = new();
    private readonly AuthService _auth;

    private NewsViewModel CreateVm(Func<Task>? onNewsCreated = null) =>
        new(_sheets.Object, _cache.Object, _auth, onNewsCreated);

    public NewsViewModelTests()
    {
        var sp = new Mock<IServiceProvider>();
        var store = new Mock<ICredentialStore>();
        _auth = new AuthService(sp.Object, store.Object);

        _sheets.Setup(s => s.GetNewsAsync()).ReturnsAsync(new List<NewsItem>());
        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(new List<Fencer>());
    }

    [Fact]
    public void InitialState_CreateCardIsCollapsedByDefault()
    {
        var vm = CreateVm();

        vm.IsCreateExpanded.Should().BeFalse();
        vm.CreateChevronText.Should().Be("\u25B8");
        vm.HasNoNews.Should().BeTrue();
    }

    [Fact]
    public void ToggleCreateCommand_FlipsExpandedAndChevron()
    {
        var vm = CreateVm();

        vm.ToggleCreateCommand.Execute(null);
        vm.IsCreateExpanded.Should().BeTrue();
        vm.CreateChevronText.Should().Be("\u25BE");

        vm.ToggleCreateCommand.Execute(null);
        vm.IsCreateExpanded.Should().BeFalse();
        vm.CreateChevronText.Should().Be("\u25B8");
    }

    [Fact]
    public async Task LoadAsync_OrdersNewsNewestFirst_AndResolvesAuthorNames()
    {
        var fencers = new List<Fencer>
        {
            new() { Id = "inst-1", Name = "Coach Anna", IsInstructor = true }
        };
        var older = new NewsItem
        {
            Id = "n1",
            Title = "Old News",
            Body = "Body 1",
            CreatedByFencerId = "inst-1",
            CreatedAtUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        var newer = new NewsItem
        {
            Id = "n2",
            Title = "New News",
            Body = "Body 2",
            CreatedByFencerId = "unknown-id",
            CreatedAtUtc = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)
        };

        _sheets.Setup(s => s.GetFencersAsync()).ReturnsAsync(fencers);
        _sheets.Setup(s => s.GetNewsAsync()).ReturnsAsync(new List<NewsItem> { older, newer });

        var vm = CreateVm();
        await vm.LoadAsync();

        _cache.Verify(c => c.InvalidateNews(), Times.Once);
        vm.News.Should().HaveCount(2);
        vm.News[0].Title.Should().Be("New News");
        vm.News[0].AuthorName.Should().Be("Instructor");
        vm.News[1].Title.Should().Be("Old News");
        vm.News[1].AuthorName.Should().Be("Coach Anna");
        vm.HasNoNews.Should().BeFalse();
    }

    [Fact]
    public async Task CreateNewsAsync_NonInstructor_DoesNothing()
    {
        _auth.RestoreSession(new Fencer { Id = "f1", Name = "Member", IsInstructor = false });
        var vm = CreateVm();
        vm.NewTitle = "Hello";
        vm.NewBody = "World";

        await vm.CreateNewsAsync();

        _sheets.Verify(s => s.AddNewsAsync(It.IsAny<NewsItem>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Body")]
    [InlineData("   ", "Body")]
    [InlineData("Title", "")]
    [InlineData("Title", "   ")]
    public async Task CreateNewsAsync_MissingTitleOrBody_SetsStatusMessage(string title, string body)
    {
        _auth.RestoreSession(new Fencer { Id = "inst-1", Name = "Coach", IsInstructor = true });
        var vm = CreateVm();
        vm.NewTitle = title;
        vm.NewBody = body;

        await vm.CreateNewsAsync();

        vm.StatusMessage.Should().Be("Title and body are required.");
        _sheets.Verify(s => s.AddNewsAsync(It.IsAny<NewsItem>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewsAsync_ValidInput_PublishesCollapsesAndInvokesCallback()
    {
        _auth.RestoreSession(new Fencer { Id = "inst-1", Name = "Coach", IsInstructor = true });
        NewsItem? added = null;
        _sheets.Setup(s => s.AddNewsAsync(It.IsAny<NewsItem>()))
            .Callback<NewsItem>(n => added = n)
            .Returns(Task.CompletedTask);

        var callbackCalled = false;
        var vm = CreateVm(() => { callbackCalled = true; return Task.CompletedTask; });
        vm.IsCreateExpanded = true;
        vm.NewTitle = "  Tournament Announcement  ";
        vm.NewBody = "  Registration is open!  ";

        await vm.CreateNewsAsync();

        added.Should().NotBeNull();
        added!.Title.Should().Be("Tournament Announcement");
        added.Body.Should().Be("Registration is open!");
        added.CreatedByFencerId.Should().Be("inst-1");

        vm.NewTitle.Should().BeEmpty();
        vm.NewBody.Should().BeEmpty();
        vm.IsCreateExpanded.Should().BeFalse();
        vm.StatusMessage.Should().Be("News published.");
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SaveNewsAsync_UpdatesExistingNewsItem()
    {
        _auth.RestoreSession(new Fencer { Id = "inst-1", Name = "Coach", IsInstructor = true });
        var item = new NewsItem { Id = "n1", Title = "Initial", Body = "Initial Body" };
        var row = new NewsRow(item, "Coach");
        row.BeginEdit();
        row.IsEditing.Should().BeTrue();
        row.EditTitle = "Updated Title";
        row.EditBody = "Updated Body";

        var vm = CreateVm();
        await vm.SaveNewsAsync(row);

        _sheets.Verify(s => s.UpsertNewsAsync(It.Is<NewsItem>(n =>
            n.Id == "n1" && n.Title == "Updated Title" && n.Body == "Updated Body")), Times.Once);
        row.IsEditing.Should().BeFalse();
        vm.StatusMessage.Should().Be("News updated.");
    }

    [Fact]
    public void NewsRow_CancelEdit_RestoresOriginalValues()
    {
        var item = new NewsItem { Id = "n1", Title = "Original Title", Body = "Original Body" };
        var row = new NewsRow(item, "Coach");

        row.BeginEdit();
        row.EditTitle = "Changed";
        row.EditBody = "Changed Body";

        row.CancelEdit();

        row.IsEditing.Should().BeFalse();
        row.IsNotEditing.Should().BeTrue();
        row.EditTitle.Should().Be("Original Title");
        row.EditBody.Should().Be("Original Body");
    }

    [Fact]
    public async Task DeleteNewsAsync_CallsServiceAndReloads()
    {
        _auth.RestoreSession(new Fencer { Id = "inst-1", Name = "Coach", IsInstructor = true });
        var item = new NewsItem { Id = "n-del", Title = "To Delete", Body = "Body" };
        var row = new NewsRow(item, "Coach");

        var vm = CreateVm();
        await vm.DeleteNewsAsync(row);

        _sheets.Verify(s => s.DeleteNewsAsync("n-del"), Times.Once);
        vm.StatusMessage.Should().Be("News deleted.");
    }
}
