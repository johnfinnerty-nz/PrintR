namespace PrintR.Agent;

public static class AgentPaths
{
    public static string ConfigDirectory => Ensure(Environment.GetEnvironmentVariable("PRINTR_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintR Agent"));
    public static string DataDirectory => Ensure(Environment.GetEnvironmentVariable("PRINTR_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintR Agent"));
    public static string SpoolDirectory => Ensure(Path.Combine(DataDirectory, "Spool"));

    private static string Ensure(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    public static void WritePrivateText(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
