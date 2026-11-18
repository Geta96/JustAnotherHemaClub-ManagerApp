using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Presentation wrapper around a <see cref="PaymentRequest"/>, used to render
/// pending/my payment requests on the Finance page (both MAUI and Web).
/// </summary>
public sealed class PaymentRequestRow
{
    public PaymentRequest Request { get; }
    public string FencerName { get; }

    public PaymentRequestRow(PaymentRequest request, string fencerName)
    {
        Request = request;
        FencerName = fencerName;
    }

    public string FencerId => Request.FencerId;
    public string AmountText => $"{Request.Amount:N0} Ft";
    public string PeriodText => $"{Request.Year}-{Request.Month:00}";
    public bool HasNote => !string.IsNullOrWhiteSpace(Request.Note);
    public string StatusText => Request.Status.ToString();
}
