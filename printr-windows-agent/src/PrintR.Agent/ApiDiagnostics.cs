namespace PrintR.Agent;

public static class ApiDiagnostics
{
    private static readonly object Gate = new();

    public static string? LastConnectionAttempt { get; private set; }
    public static string? LastApiError { get; private set; }

    public static void RecordConnection(string? remoteAddress)
    {
        lock (Gate)
        {
            LastConnectionAttempt = $"{DateTimeOffset.Now:u} {remoteAddress}";
        }
    }

    public static void RecordError(string message)
    {
        lock (Gate)
        {
            LastApiError = $"{DateTimeOffset.Now:u} {message}";
        }
    }
}
