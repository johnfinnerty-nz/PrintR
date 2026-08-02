using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class SpoolCleanupTests
{
    [Fact]
    public void Cleanup_Removes_Temporary_Files_After_Success()
    {
        var dir = Directory.CreateTempSubdirectory("printr-cleanup-").FullName;
        File.WriteAllText(Path.Combine(dir, "proposal.docx"), "test");
        File.WriteAllText(Path.Combine(dir, "proposal.pdf"), "test");

        SpoolCleanup.CleanupJobFolder(dir, new AgentSettings("token", 8787, false));

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Debug_Mode_Preserves_Temporary_Files()
    {
        var dir = Directory.CreateTempSubdirectory("printr-keep-").FullName;
        File.WriteAllText(Path.Combine(dir, "proposal.docx"), "test");

        SpoolCleanup.CleanupJobFolder(dir, new AgentSettings("token", 8787, true));

        Assert.True(Directory.Exists(dir));
        Directory.Delete(dir, recursive: true);
    }
}
