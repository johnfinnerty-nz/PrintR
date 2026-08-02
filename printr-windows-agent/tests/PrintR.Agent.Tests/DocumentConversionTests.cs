using Microsoft.Extensions.Logging.Abstractions;
using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class DocumentConversionTests
{
    [Fact]
    public void LibreOffice_Detection_Uses_Configured_Path()
    {
        var settings = new AgentSettings("token", 8787, false, @"C:\Tools\LibreOffice\soffice.exe");

        var path = LibreOfficeDocumentConverter.ResolvePath(settings.LibreOfficePath, null, p => p == settings.LibreOfficePath);

        Assert.Equal(settings.LibreOfficePath, path);
    }

    [Fact]
    public async Task Conversion_Fails_Clearly_When_No_Backend_Can_Convert()
    {
        var converter = new CompositeDocumentConverter([]);

        var result = await converter.ConvertAsync("file.docx", Path.GetTempPath(), "pdf", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("LibreOffice", result.Message);
    }

    [Fact]
    public async Task LibreOffice_Returns_Clear_Error_When_Not_Detected()
    {
        var converter = new LibreOfficeDocumentConverter(
            new AgentSettings("token", 8787, false),
            NullLogger<LibreOfficeDocumentConverter>.Instance,
            _ => false,
            TimeSpan.FromSeconds(1));

        var result = await converter.ConvertAsync("file.docx", Path.GetTempPath(), "pdf", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("LibreOffice", result.Message);
    }

    [Fact]
    public void Job_Status_Transitions_Include_Converting()
    {
        var store = new JobStore();
        var job = store.AddQueued(null, "proposal.docx", "docx");

        store.Update(job.JobId, JobStatus.Converting, "Converting DOCX to PDF");

        Assert.Equal(JobStatus.Converting, store.Get(job.JobId)!.Status);
    }

    [Fact]
    public void Missing_Generated_Pdf_Is_Treated_As_Failure()
    {
        var result = LibreOfficeDocumentConverter.ValidateExpectedOutput(@"C:\Temp\missing.pdf", _ => false);

        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("generated PDF", result.Message);
    }

    [Fact]
    public void Pdf_Print_Tool_Uses_Configured_Path()
    {
        var path = PdfPrintTool.ResolvePath(
            @"C:\Tools\SumatraPDF.exe",
            file => file == @"C:\Tools\SumatraPDF.exe",
            null);

        Assert.Equal(@"C:\Tools\SumatraPDF.exe", path);
    }

    [Fact]
    public void Pdf_Print_Tool_Can_Be_Found_On_Path()
    {
        var path = PdfPrintTool.ResolvePath(
            null,
            file => file == @"C:\Portable\SumatraPDF.exe",
            @"C:\Portable");

        Assert.Equal(@"C:\Portable\SumatraPDF.exe", path);
    }
}
