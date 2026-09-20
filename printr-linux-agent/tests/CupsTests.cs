using PrintR.Agent;
using Xunit;

public class CupsTests
{
    [Fact]
    public void Parses_Cups_Queues_And_Default()
    {
        var printers = PrinterService.ParsePrinters("printer Studio is idle. enabled since yesterday\nprinter Lab disabled since today\nsystem default destination: Studio\n");
        Assert.Equal(2, printers.Count); Assert.True(printers[0].IsDefault); Assert.Equal("Disabled", printers[1].Status);
    }
    [Fact]
    public void Passes_Options_Without_A_Shell()
    {
        var args = PrinterService.BuildArguments("report.pdf", new PrintRequest("Office printer", 2, "bw", true, "1-3, 7", "shortedge", "landscape", "letter"));
        Assert.Contains("Office printer", args); Assert.Contains("sides=two-sided-short-edge", args);
        Assert.Contains("page-ranges=1-3,7", args); Assert.Contains("print-color-mode=monochrome", args); Assert.Contains("--", args);
    }
}
