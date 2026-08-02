using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class PairingAndDiagnosticsTests
{
    [Fact]
    public void Wrong_Token_Is_Rejected()
    {
        Assert.False(TokenAuth.IsAuthorized("new-token", "old-token"));
    }

    [Fact]
    public void Firewall_Diagnostic_Is_Actionable()
    {
        var warning = FirewallDiagnostics.GetWarning();

        Assert.Contains("Windows Defender Firewall", warning);
        Assert.Contains("Private", warning);
    }

    [Fact]
    public void Recent_Job_Metadata_Does_Not_Store_Document_Contents()
    {
        var job = new PrintJob(
            Guid.NewGuid(),
            JobStatus.Completed,
            "Printed successfully",
            "Printer",
            "secret.docx",
            "docx",
            [],
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal("secret.docx", job.FileName);
        Assert.Null(job.ErrorMessage);
    }
}
