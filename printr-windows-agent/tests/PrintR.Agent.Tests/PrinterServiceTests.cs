using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class PrinterServiceTests
{
    [Fact]
    public void Pdf_Args_Include_Duplex_When_Requested()
    {
        var request = new PrintRequest("Office Printer", 2, "color", true, "1-3");

        var args = PrinterService.BuildPdfArgs(@"C:\Temp\job.pdf", request).ToList();

        Assert.Contains("-print-settings", args);
        Assert.Contains("2x,1-3,duplexlong,color,paper=A4", args);
        Assert.Contains("-print-to", args);
        Assert.Contains("Office Printer", args);
    }

    [Fact]
    public void Pdf_Args_Include_Simplex_When_Duplex_Is_Off()
    {
        var request = new PrintRequest(null, 1, "bw", false, null);

        var args = PrinterService.BuildPdfArgs(@"C:\Temp\job.pdf", request).ToList();

        Assert.Contains("1x,simplex,monochrome,paper=A4", args);
        Assert.Contains("-print-to-default", args);
    }

    [Fact]
    public void Pdf_Args_Include_ShortEdge_And_Landscape_Letter()
    {
        var request = new PrintRequest(null, 1, "color", true, null, "shortedge", "landscape", "letter");

        var args = PrinterService.BuildPdfArgs(@"C:\Temp\job.pdf", request).ToList();

        Assert.Contains("1x,duplexshort,color,paper=Letter,landscape", args);
    }
}
