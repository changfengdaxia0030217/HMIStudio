using HMI.Server;
using Xunit;

namespace HMI.Core.Tests;

public sealed class CsvSafetyTests
{
    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+SUM(A1:A2)", "\"'+SUM(A1:A2)\"")]
    [InlineData("-10", "\"'-10\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("  =cmd", "\"'  =cmd\"")]
    public void EscapeFieldNeutralizesSpreadsheetFormulaPrefixes(string value, string expected)
    {
        Assert.Equal(expected, CsvSafety.EscapeField(value));
    }

    [Fact]
    public void EscapeFieldQuotesEmbeddedQuotesAndPreservesNormalText()
    {
        Assert.Equal("\"Pump \"\"A\"\"\"", CsvSafety.EscapeField("Pump \"A\""));
        Assert.Equal("\"normal text\"", CsvSafety.EscapeField("normal text"));
    }

    [Fact]
    public void EscapeFieldHandlesNullAndEmptyValues()
    {
        Assert.Equal("\"\"", CsvSafety.EscapeField(null));
        Assert.Equal("\"\"", CsvSafety.EscapeField(string.Empty));
    }
}