using System.Globalization;
using Google;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public partial class GoogleSheetsService
{
    // --- PaymentRequests ---
    // Columns: A=Id, B=FencerId, C=Year, D=Month, E=Amount, F=Note,
    //          G=Status, H=RequestedByFencerId, I=RequestedAtUtc,
    //          J=ReviewedByInstructorId, K=ReviewedAtUtc
    private const string PaymentRequestsRange = "PaymentRequests!A2:K";

    public async Task<List<PaymentRequest>> GetPaymentRequestsAsync()
    {
        IList<IList<object>> rows;
        try
        {
            rows = await ReadUnformattedAsync(PaymentRequestsRange);
        }
        catch (GoogleApiException)
        {
            return new List<PaymentRequest>();
        }

        var list = new List<PaymentRequest>();
        foreach (var r in rows)
        {
            var id = SheetRowMapper.S(r, 0);
            if (string.IsNullOrWhiteSpace(id)) continue;

            var status = Enum.TryParse<PaymentRequestStatus>(SheetRowMapper.S(r, 6), true, out var st)
                ? st
                : PaymentRequestStatus.Pending;

            list.Add(new PaymentRequest
            {
                Id = id,
                FencerId = SheetRowMapper.S(r, 1),
                Year = int.TryParse(SheetRowMapper.S(r, 2), out var y) ? y : 0,
                Month = int.TryParse(SheetRowMapper.S(r, 3), out var m) ? m : 0,
                Amount = decimal.TryParse(SheetRowMapper.S(r, 4), NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : 0m,
                Note = SheetRowMapper.S(r, 5),
                Status = status,
                RequestedByFencerId = SheetRowMapper.S(r, 7),
                RequestedAtUtc = SheetRowMapper.TryParseDate(SheetRowMapper.S(r, 8), out var reqAt)
                    ? DateTime.SpecifyKind(reqAt, DateTimeKind.Utc)
                    : DateTime.UtcNow,
                ReviewedByInstructorId = string.IsNullOrWhiteSpace(SheetRowMapper.S(r, 9)) ? null : SheetRowMapper.S(r, 9),
                ReviewedAtUtc = SheetRowMapper.TryParseDate(SheetRowMapper.S(r, 10), out var revAt)
                    ? DateTime.SpecifyKind(revAt, DateTimeKind.Utc)
                    : null
            });
        }
        return list;
    }

    public async Task UpsertPaymentRequestAsync(PaymentRequest req)
    {
        IList<IList<object>> rows;
        try { rows = await ReadAsync(PaymentRequestsRange); }
        catch (GoogleApiException) { rows = new List<IList<object>>(); }

        int rowIndex = -1;
        for (int i = 0; i < rows.Count; i++)
            if (SheetRowMapper.S(rows[i], 0) == req.Id) { rowIndex = i; break; }

        var values = new List<object>
        {
            req.Id,
            req.FencerId,
            req.Year,
            req.Month,
            req.Amount.ToString(CultureInfo.InvariantCulture),
            req.Note ?? "",
            req.Status.ToString(),
            req.RequestedByFencerId ?? "",
            req.RequestedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            req.ReviewedByInstructorId ?? "",
            req.ReviewedAtUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? ""
        };

        if (rowIndex >= 0)
            await UpdateAsync($"PaymentRequests!A{rowIndex + 2}:K{rowIndex + 2}", values);
        else
            await AppendAsync("PaymentRequests!A1", values);
    }
}