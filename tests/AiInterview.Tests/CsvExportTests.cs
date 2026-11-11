using AiInterview.Web.Services;
using Xunit;

namespace AiInterview.Tests;

public sealed class CsvExportTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\",\"click\")")]
    [InlineData("+SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("  =1+1")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    [InlineData("\u00a0=1+1")]
    public void UntrustedSpreadsheetFormulaIsNeutralized(string value)
    {
        var cell = LecturerService.CsvCell(value);
        Assert.StartsWith("\"'", cell);
        Assert.Equal("'" + value, ReadSingleCell(cell));
    }

    [Theory]
    [InlineData("Nguyễn Văn An")]
    [InlineData("An, Bình")]
    [InlineData("Sinh viên \"A\"")]
    [InlineData("Dòng 1\r\nDòng 2")]
    [InlineData("10.00")]
    [InlineData("")]
    public void PlainTextRoundTripsWithoutAddingColumnsOrRows(string value)
    {
        Assert.Equal(value, ReadSingleCell(LecturerService.CsvCell(value)));
    }

    private static string ReadSingleCell(string csv)
    {
        Assert.StartsWith("\"", csv);
        Assert.EndsWith("\"", csv);
        var result = new System.Text.StringBuilder();
        for (var i = 1; i < csv.Length - 1; i++)
        {
            if (csv[i] == '"')
            {
                Assert.True(i + 1 < csv.Length - 1 && csv[i + 1] == '"', "An embedded quote must be escaped inside the same CSV cell.");
                i++;
            }
            result.Append(csv[i]);
        }
        return result.ToString();
    }
}
