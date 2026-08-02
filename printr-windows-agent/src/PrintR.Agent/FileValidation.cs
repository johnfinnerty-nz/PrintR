namespace PrintR.Agent;

public sealed class FileValidation
{
    public const long MaxUploadBytes = 100L * 1024L * 1024L;

    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".txt"] = ["text/plain", "application/octet-stream"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/octet-stream"]
    };

    private static readonly HashSet<string> RejectedOfficeMacroExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docm",
        ".dotm",
        ".xlsm",
        ".pptm"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } = [".pdf", ".png", ".jpg", ".jpeg", ".txt", ".docx"];

    public static string SanitizeFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? "upload.bin");
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(safe) ? "upload.bin" : safe;
    }

    public static (bool Ok, string Message) Validate(string fileName, string? contentType, long length)
    {
        if (length <= 0) return (false, "Uploaded file is empty.");
        if (length > MaxUploadBytes) return (false, "Uploaded file exceeds the 100 MB limit.");

        var extension = Path.GetExtension(fileName);
        if (RejectedOfficeMacroExtensions.Contains(extension))
        {
            return (false, "Macro-enabled Office files are not supported. PrintR accepts .docx but rejects .docm, .dotm, .xlsm, and .pptm.");
        }

        if (string.IsNullOrWhiteSpace(extension) || !Allowed.TryGetValue(extension, out var mimeTypes))
        {
            return (false, "Unsupported file type. PrintR Agent currently supports PDF, PNG, JPG/JPEG, TXT, and DOCX.");
        }

        if (!string.IsNullOrWhiteSpace(contentType) &&
            !mimeTypes.Contains(contentType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"MIME type '{contentType}' does not match supported type for {extension}.");
        }

        return (true, "File accepted.");
    }
}
