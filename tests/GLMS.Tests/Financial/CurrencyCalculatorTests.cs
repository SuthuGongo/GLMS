using GLMS.Core.Patterns.Strategy;

namespace GLMS.Tests.Financial;

/// <summary>"Currency Calculation: verify that the maths converting USD to ZAR is correct, given a specific rate."</summary>
public class CurrencyCalculatorTests
{
    [Theory]
    [InlineData("100", "18.50", "1850.00")]
    [InlineData("1", "18.4523", "18.45")]
    [InlineData("1250.75", "18.2", "22763.65")]
    [InlineData("0.01", "18.50", "0.19")]          // 0.185 -> 0.19 (half away from zero)
    [InlineData("999999.99", "19.1234", "19123399.81")]
    public void Convert_GivenSpecificRate_ReturnsCorrectZarAmount(string usd, string rate, string expectedZar)
    {
        var result = CurrencyCalculator.Convert(decimal.Parse(usd, System.Globalization.CultureInfo.InvariantCulture),
                                                decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expectedZar, System.Globalization.CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void Convert_MidpointValue_RoundsAwayFromZero_NotBankersRounding()
    {
        // 1 x 18.445 = 18.445 -> 18.45 (banker's rounding would give 18.44)
        Assert.Equal(18.45m, CurrencyCalculator.Convert(1m, 18.445m));
    }

    [Fact]
    public void Convert_UsesDecimalPrecision_NoFloatingPointDrift()
    {
        // With double, 0.1 * 3 style maths drifts; decimal must be exact.
        Assert.Equal(55.50m, CurrencyCalculator.Convert(3m, 18.50m));
        Assert.Equal(0.30m, CurrencyCalculator.Convert(0.1m, 3m));
    }

    [Fact]
    public void Convert_ZeroAmount_ReturnsZero()
    {
        Assert.Equal(0m, CurrencyCalculator.Convert(0m, 18.50m));
    }

    [Fact]
    public void Convert_NegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyCalculator.Convert(-1m, 18.50m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-18.5")]
    public void Convert_ZeroOrNegativeRate_Throws(string rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CurrencyCalculator.Convert(100m, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" zar ", "ZAR")]
    public void NormaliseCurrencyCode_TrimsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, CurrencyCalculator.NormaliseCurrencyCode(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("DOLLAR")]
    [InlineData("12A")]
    [InlineData(null)]
    public void NormaliseCurrencyCode_InvalidCode_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => CurrencyCalculator.NormaliseCurrencyCode(input));
    }
}
