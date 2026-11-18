namespace JustAnotherHemaClub.Models;

public enum PaymentRequestStatus
{
    Pending,
    Accepted,
    Rejected
}

public class PaymentRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FencerId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Pending;
    public string RequestedByFencerId { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedByInstructorId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}