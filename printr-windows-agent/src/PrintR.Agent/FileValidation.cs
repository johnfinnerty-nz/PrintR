using System.IO.Compression;
using System.Text;

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
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        [".pptx"] = ["application/vnd.openxmlformats-officedocument.presentationml.presentation"],
        [".odt"] = ["application/vnd.oasis.opendocument.text"],
        [".ods"] = ["application/vnd.oasis.opendocument.spreadsheet"],
        [".odp"] = ["application/vnd.oasis.opendocument.presentation"],
        [".rtf"] = ["application/rtf", "text/rtf"],
        [".csv"] = ["text/csv", "text/plain", "application/csv", "application/vnd.ms-excel"],
        [".bmp"] = ["image/bmp", "image/x-ms-bmp"]
    };

    private static readonly HashSet<string> RejectedOfficeMacroExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docm",
        ".dotm",
        ".xlsm",
        ".pptm"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } = Allowed.Keys.ToArray();
    public static bool RequiresConversion(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp" or ".rtf";

    public static string SanitizeFileName(string? fileName)
    {
        var safe = (fileName ?? "upload.bin").Replace('\\', '/').Split('/').Last();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }
        safe = new string(safe.Select(c => c < 32 || "<>:\"|?*".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? "upload.bin" : safe.Length > 160 ? safe[..120] + Path.GetExtension(safe) : safe;
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
            return (false, "Unsupported file type. Supported: " + string.Join(", ", SupportedExtensions));
        }

        if (!string.IsNullOrWhiteSpace(contentType) &&
            !string.Equals(contentType.Split(';')[0].Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase) &&
            !mimeTypes.Contains(contentType.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"MIME type '{contentType}' does not match supported type for {extension}.");
        }

        return (true, "File accepted.");
    }

    public static (bool Ok, string Message) ValidateContent(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            var length = stream.Read(header);
            var bytes = header[..length];
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var signatureOk = extension switch
            {
                ".pdf" => bytes.StartsWith("%PDF-"u8),
                ".png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                ".jpg" or ".jpeg" => bytes.StartsWith(new byte[] { 255, 216, 255 }),
                ".bmp" => bytes.StartsWith("BM"u8),
                ".rtf" => bytes.StartsWith("{\\rtf"u8),
                _ => true
            };
            if (!signatureOk) return (false, "File content does not match its extension.");
            if (extension is ".txt" or ".csv")
            {
                stream.Position = 0;
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                var buffer = new char[4096];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    if (Array.IndexOf(buffer, '\0', 0, read) >= 0) return (false, "Text files must not contain binary data.");
            }
            if (extension is ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp")
            {
                stream.Position = 0;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                if (archive.Entries.Count > 10000 || archive.Entries.Sum(e => e.Length) > 500L * 1024 * 1024)
                    return (false, "Document archive exceeds the safe expansion limit.");
                if (archive.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.StartsWith("Basic/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase)))
                    return (false, "Documents containing embedded macros are not supported.");
                var required = extension switch { ".docx" => "word/document.xml", ".xlsx" => "xl/workbook.xml", ".pptx" => "ppt/presentation.xml", _ => "content.xml" };
                if (archive.GetEntry(required) is null) return (false, "Document structure does not match its extension.");
                if (extension is ".odt" or ".ods" or ".odp")
                {
                    var entry = archive.GetEntry("mimetype");
                    if (entry is null || entry.Length > 200) return (false, "OpenDocument MIME metadata is missing.");
                    using var mimeReader = new StreamReader(entry.Open());
                    if (mimeReader.ReadToEnd() != Allowed[extension][0]) return (false, "OpenDocument type does not match its extension.");
                }
            }
            return (true, "File content accepted.");
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or IOException)
        { return (false, "The document is damaged or has an unsupported encoding."); }
    }
}
