namespace PrintR.Agent;

public static class SpoolCleanup
{
    public static bool ShouldKeepFiles(AgentSettings settings) =>
        settings.DebugKeepSpoolFiles ||
        string.Equals(Environment.GetEnvironmentVariable("PRINTR_KEEP_SPOOL"), "true", StringComparison.OrdinalIgnoreCase);

    public static void CleanupJobFolder(string jobDirectory, AgentSettings settings)
    {
        if (ShouldKeepFiles(settings) || !Directory.Exists(jobDirectory))
        {
            return;
        }

        Directory.Delete(jobDirectory, recursive: true);
    }
}
