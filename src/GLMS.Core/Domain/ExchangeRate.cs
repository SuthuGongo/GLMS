namespace GLMS.Core.Domain;

/// <summary>
/// One published exchange rate (table ExchangeRates), e.g. USD -> ZAR 18.4523 from ExchangeRate-API,
/// published 13 Oct 2026 02:00. Each published rate is stored once (unique on currencies + publish time + rate)
/// and every service request costed with it references the same row.
/// </summary>
public class ExchangeRate
{
    public int Id { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public string QuoteCurrency { get; set; } = "ZAR";
    public decimal Rate { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime PublishedUtc { get; set; }

    /// <summary>
    /// The database stores times to the whole second (datetime2(0)); trimming here means a rate found in
    /// memory always matches the stored row exactly, so the same published rate is never inserted twice.
    /// </summary>
    public static DateTime ToStoredPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    /// <summary>True when this row describes the same published rate (used to reuse an existing row).</summary>
    public bool IsSameRateAs(ExchangeRate other) =>
        other is not null &&
        string.Equals(BaseCurrency, other.BaseCurrency, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(QuoteCurrency, other.QuoteCurrency, StringComparison.OrdinalIgnoreCase) &&
        Rate == other.Rate &&
        PublishedUtc == other.PublishedUtc;
}
