using System.IO.Compression;
using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class DeploymentTests
{
    [Theory]
    [InlineData("report.xlsx")]
    [InlineData("slides.pptx")]
    [InlineData("letter.odt")]
    [InlineData("budget.ods")]
    [InlineData("slides.odp")]
    [InlineData("letter.rtf")]
    [InlineData("data.csv")]
    [InlineData("scan.bmp")]
    public void Accepts_Expanded_Formats_With_Generic_Provider_Mime(string file) => Assert.True(FileValidation.Validate(file, "application/octet-stream", 12).Ok);

    [Theory]
    [InlineData("1-3,7", true)]
    [InlineData("", true)]
    [InlineData("0", false)]
    [InlineData("4-1", false)]
    [InlineData("1,duplex", false)]
    [InlineData("1-2-3", false)]
    [InlineData("2147483648", false)]
    public void Checks_Page_Ranges(string range, bool expected) => Assert.Equal(expected, PrintRequestValidation.IsPageRangeValid(range));

    [Fact]
    public void Rejects_Spoofed_Pdf()
    {
        var path = Path.Combine(AgentPaths.DataDirectory, "fake.pdf"); File.WriteAllText(path, "not a PDF");
        Assert.False(FileValidation.ValidateContent(path).Ok);
    }

    [Fact]
    public void Rejects_Office_Archive_Without_Expected_Document()
    {
        var path = Path.Combine(AgentPaths.DataDirectory, "fake.xlsx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("word/document.xml");
        Assert.False(FileValidation.ValidateContent(path).Ok);
    }

    [Fact]
    public void Rejects_Embedded_Macros_In_Renamed_Document()
    {
        var path = Path.Combine(AgentPaths.DataDirectory, "macro.docx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { zip.CreateEntry("word/document.xml"); zip.CreateEntry("word/vbaProject.bin"); }
        Assert.False(FileValidation.ValidateContent(path).Ok);
    }

    [Fact]
    public void Settings_Persist_And_Reject_Invalid_Values()
    {
        var store = new AgentSettingsStore();
        store.UpdatePreferences("Studio", 8899, null, null, 25, 90, false, true);
        Assert.Equal(25, new AgentSettingsStore().Load().MaxUploadMegabytes);
        Assert.Equal("Studio", store.Load().FriendlyName);
        Assert.Throws<ArgumentException>(() => store.UpdatePreferences("Studio", 80, null, null, 25, 90, false, true));
    }

    [Fact]
    public void Interrupted_Jobs_Are_Not_Reported_As_Still_Printing_After_Restart()
    {
        var store = new JobStore(); var job = store.AddQueued(null, "interrupted.txt", "txt");
        store.Update(job.JobId, JobStatus.Printing, "Printing");
        Assert.Equal(JobStatus.Failed, new JobStore().Get(job.JobId)!.Status);
    }
}
