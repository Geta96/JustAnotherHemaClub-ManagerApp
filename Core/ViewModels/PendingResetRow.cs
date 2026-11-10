using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>One pending instructor-mediated password reset shown on the Fencers page.</summary>
public partial class PendingResetRow
{
    public Fencer Fencer { get; }

    public string Name => Fencer.DisplayName;
    public string UsernameText => string.IsNullOrWhiteSpace(Fencer.Username) ? "" : $"@{Fencer.Username}";
    public string RequestedText => Fencer.PasswordResetRequestedAtUtc is { } at
        ? $"Requested {at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
        : "Requested";

    // Parent-supplied handlers so buttons in the DataTemplate bind directly to the row.
    public Func<PendingResetRow, Task>? ApproveAction { get; set; }
    public Func<PendingResetRow, Task>? RejectAction { get; set; }

    [RelayCommand] private Task Approve() => ApproveAction?.Invoke(this) ?? Task.CompletedTask;
    [RelayCommand] private Task Reject() => RejectAction?.Invoke(this) ?? Task.CompletedTask;

    public PendingResetRow(Fencer fencer) => Fencer = fencer;
}