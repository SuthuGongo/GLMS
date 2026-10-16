using System.Text.RegularExpressions;

namespace GLMS.Core.Patterns.Strategy;

public static partial class CurrencyCalculator
{
    public const int MoneyDecimals = 2;

    public const int RateDecimals = 6;

    public static decimal NormaliseRate(decimal rate)
    {
        if (rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Exchange rate must be greater than zero.");
        var rounded = Math.Round(rate, RateDecimals, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Exchange rate is too small to store.");
        return rounded;
    }
    public static decimal Convert(decimal amount, decimal rate)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");
        if (rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Exchange rate must be greater than zero.");

        return Math.Round(amount * rate, MoneyDecimals, MidpointRounding.AwayFromZero);
    }
    public static string NormaliseCurrencyCode(string? code)
    {
        var normalised = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CurrencyCodeRegex().IsMatch(normalised))
            throw new ArgumentException($"'{code}' is not a valid 3-letter ISO currency code.", nameof(code));
        return normalised;
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodeRegex();
}
