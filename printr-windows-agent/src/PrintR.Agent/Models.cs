namespace PrintR.Agent;

public enum JobStatus
{
    Queued,
    Receiving,
    Validating,
    Converting,
    Converted,
    Printing,
    Completed,
    Failed,
    Cancelled
}

public sealed record PrinterInfo(string Name, bool IsDefault, string Status);

public sealed record PrintJob(
    Guid JobId,
    JobStatus Status,
    string Message,
    string? PrinterName,
    string? FileName,
    string? FileType,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record PrintRequest(
    string? PrinterName,
    int Copies,
    string ColorMode,
    bool Duplex,
    string? PageRange,
    string DuplexMode = "none",
    string Orientation = "portrait",
    string PaperSize = "a4");

public sealed record AgentSettings(
    string Token,
    int Port,
    bool DebugKeepSpoolFiles,
    string? LibreOfficePath = null,
    bool WordComEnabled = false,
    string? InstanceId = null,
    bool StartWithWindows = false,
    string? FriendlyName = null,
    bool MockPrintMode = false,
    string? TlsCertificatePassword = null);

public sealed record PairingPayload(
    string App,
    string Version,
    string InstanceId,
    string ComputerName,
    string? Hostname,
    string IpAddress,
    int Port,
    string Token,
    string Scheme = "http",
    string? TlsFingerprint = null);

public sealed record DocumentConversionResult(
    bool Success,
    string? OutputPath,
    string Message,
    IReadOnlyList<string> Warnings);

public sealed record ConversionBackendStatus(
    bool Available,
    string? Path = null,
    bool Enabled = true,
    string? Message = null);
