namespace JustAnotherHemaClub.Models;

public class PasswordReset
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedUtc { get; set; }
    public int RowIndex { get; set; } = -1;   // 1-based sheet row of this record
}