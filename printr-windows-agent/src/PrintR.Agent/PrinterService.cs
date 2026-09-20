using System.Drawing;
using System.Drawing.Printing;
using System.Text;

namespace PrintR.Agent;

public sealed class PrinterService(ILogger<PrinterService> logger, IDocumentConverter converter, AgentSettingsStore settingsStore) : IPrinterService
{
    private string? PdfCommand => PdfPrintTool.ResolvePath(settingsStore.Load().PdfToolPath ?? Environment.GetEnvironmentVariable("PRINTR_PDF_COMMAND"), File.Exists, Environment.GetEnvironmentVariable("PATH"));

    private bool MockPrint => settingsStore.Load().MockPrintMode ||
                               string.Equals(Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT"), "true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        return PrinterSettings.InstalledPrinters
            .Cast<string>()
            .Select(name => new PrinterInfo(name, IsDefaultPrinter(name), "Ready"))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> PrintAsync(
        string path,
        PrintRequest request,
        CancellationToken cancellationToken,
        Action<JobStatus, string>? updateStatus = null)
    {
        if (MockPrint)
        {
            await Task.Delay(250, cancellationToken);
            return ["Mock print mode: job accepted but not sent to a printer."];
        }

        if (!PrintRequestValidation.IsPageRangeValid(request.PageRange)) throw new ArgumentException("Invalid page range.");
        if (FileValidation.RequiresConversion(path)) return await PrintDocxAsync(path, request, cancellationToken, updateStatus);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".txt" or ".csv" => PrintText(path, request),
            ".png" or ".jpg" or ".jpeg" or ".bmp" => PrintImage(path, request),
            ".pdf" => await PrintPdfAsync(path, request, cancellationToken, requireReliableTool: true),
            ".docx" => await PrintDocxAsync(path, request, cancellationToken, updateStatus),
            _ => throw new NotSupportedException("Unsupported file type.")
        };
    }

    private static bool IsDefaultPrinter(string printerName)
    {
        var settings = new PrinterSettings();
        return string.Equals(settings.PrinterName, printerName, StringComparison.OrdinalIgnoreCase);
    }

    private static PrintDocument CreateDocument(PrintRequest request)
    {
        var doc = new PrintDocument();
        if (!string.IsNullOrWhiteSpace(request.PrinterName))
        {
            doc.PrinterSettings.PrinterName = request.PrinterName;
        }
        if (!doc.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException($"Printer '{request.PrinterName ?? "default"}' is unavailable.");
        }
        doc.PrinterSettings.Copies = (short)Math.Clamp(request.Copies, 1, 99);
        doc.PrinterSettings.Duplex = ResolveDuplex(request);
        doc.DefaultPageSettings.Landscape = string.Equals(request.Orientation, "landscape", StringComparison.OrdinalIgnoreCase);
        doc.DefaultPageSettings.Color = request.ColorMode == "color";

        var requestedKind = string.Equals(request.PaperSize, "letter", StringComparison.OrdinalIgnoreCase)
            ? PaperKind.Letter
            : PaperKind.A4;
        var paper = doc.PrinterSettings.PaperSizes
            .Cast<PaperSize>()
            .FirstOrDefault(size => size.Kind == requestedKind);
        if (paper is not null)
        {
            doc.DefaultPageSettings.PaperSize = paper;
        }
        return doc;
    }

    private static Duplex ResolveDuplex(PrintRequest request) => request.DuplexMode.ToLowerInvariant() switch
    {
        "shortedge" => Duplex.Horizontal,
        "longedge" => Duplex.Vertical,
        _ => request.Duplex ? Duplex.Vertical : Duplex.Simplex
    };

    private static IReadOnlyList<string> PrintText(string path, PrintRequest request)
    {
        var warnings = OptionWarnings(request);
        var text = File.ReadAllText(path, Encoding.UTF8);
        using var doc = CreateDocument(request);
        using var font = new Font("Consolas", 10);
        var offset = 0;
        doc.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("The printer did not provide a graphics surface.");
            var chars = 0;
            var lines = 0;
            graphics.MeasureString(text[offset..], font, e.MarginBounds.Size, StringFormat.GenericTypographic, out chars, out lines);
            if (chars == 0 && offset < text.Length) throw new InvalidOperationException("The selected paper has no printable text area.");
            graphics.DrawString(text[offset..(offset + chars)], font, Brushes.Black, e.MarginBounds, StringFormat.GenericTypographic);
            offset += chars;
            e.HasMorePages = offset < text.Length;
        };
        doc.Print();
        return warnings;
    }

    private static IReadOnlyList<string> PrintImage(string path, PrintRequest request)
    {
        var warnings = OptionWarnings(request);
        using var image = Image.FromFile(path);
        using var doc = CreateDocument(request);
        doc.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("The printer did not provide a graphics surface.");
            var area = e.MarginBounds;
            var ratio = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
            var width = (int)(image.Width * ratio);
            var height = (int)(image.Height * ratio);
            var left = area.Left + (area.Width - width) / 2;
            var top = area.Top + (area.Height - height) / 2;
            graphics.DrawImage(image, left, top, width, height);
        };
        doc.Print();
        return warnings;
    }

    private async Task<IReadOnlyList<string>> PrintPdfAsync(
        string path,
        PrintRequest request,
        CancellationToken cancellationToken,
        bool requireReliableTool)
    {
        var warnings = OptionWarnings(request).ToList();
        var pdfCommand = PdfCommand;
        if (!string.IsNullOrWhiteSpace(pdfCommand) && File.Exists(pdfCommand))
        {
            // External PDF printing is intentionally limited to a configured or detected executable path.
            // User-provided values are passed via ArgumentList without invoking a shell.
            var args = BuildPdfArgs(path, request);
            await ProcessRunner.RunAsync(pdfCommand, args, cancellationToken);
            return warnings;
        }

        if (requireReliableTool)
        {
            throw new InvalidOperationException(PdfPrintTool.UnavailableMessage);
        }

        try
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Verb = string.IsNullOrWhiteSpace(request.PrinterName) ? "print" : "printto",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(request.PrinterName))
            {
                start.Arguments = '"' + request.PrinterName.Replace("\"", "", StringComparison.Ordinal) + '"';
            }
            using var process = System.Diagnostics.Process.Start(start);
            warnings.Add("Used Windows shell PDF printing fallback. Install SumatraPDF or configure PRINTR_PDF_COMMAND for reliable copies, color, duplex, paper, orientation, and page range settings.");
            return warnings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF printing is unavailable");
            throw new InvalidOperationException(PdfPrintTool.UnavailableMessage, ex);
        }
    }

    private async Task<IReadOnlyList<string>> PrintDocxAsync(
        string path,
        PrintRequest request,
        CancellationToken cancellationToken,
        Action<JobStatus, string>? updateStatus)
    {
        updateStatus?.Invoke(JobStatus.Converting, "Converting document to PDF");
        var conversion = await converter.ConvertAsync(path, Path.GetDirectoryName(path)!, "pdf", cancellationToken);
        if (!conversion.Success || string.IsNullOrWhiteSpace(conversion.OutputPath))
        {
            throw new InvalidOperationException(conversion.Message);
        }

        updateStatus?.Invoke(JobStatus.Converted, "Document converted to PDF");
        updateStatus?.Invoke(JobStatus.Printing, "Printing converted PDF");
        var warnings = conversion.Warnings.ToList();
        warnings.AddRange(await PrintPdfAsync(conversion.OutputPath, request, cancellationToken, requireReliableTool: true));
        return warnings;
    }

    internal static IEnumerable<string> BuildPdfArgs(string path, PrintRequest request)
    {
        yield return "-print-settings";
        var settings = new List<string> { $"{Math.Clamp(request.Copies, 1, 99)}x" };
        if (!string.IsNullOrWhiteSpace(request.PageRange)) settings.Add(request.PageRange);
        settings.Add(request.DuplexMode.ToLowerInvariant() switch
        {
            "shortedge" => "duplexshort",
            "longedge" => "duplexlong",
            _ => request.Duplex ? "duplexlong" : "simplex"
        });
        settings.Add(string.Equals(request.ColorMode, "bw", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.ColorMode, "blackAndWhite", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(request.ColorMode, "monochrome", StringComparison.OrdinalIgnoreCase)
            ? "monochrome"
            : "color");
        settings.Add(string.Equals(request.PaperSize, "letter", StringComparison.OrdinalIgnoreCase) ? "paper=Letter" : "paper=A4");
        if (string.Equals(request.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)) settings.Add("landscape");
        yield return string.Join(",", settings);
        if (!string.IsNullOrWhiteSpace(request.PrinterName))
        {
            yield return "-print-to";
            yield return request.PrinterName;
        }
        else
        {
            yield return "-print-to-default";
        }
        yield return "-silent";
        yield return path;
    }

    private static IReadOnlyList<string> OptionWarnings(PrintRequest request)
    {
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ColorMode)) warnings.Add("Color mode support depends on the printer driver and may be ignored.");
        if (!string.IsNullOrWhiteSpace(request.PageRange)) warnings.Add("Page range support is only available for configured PDF command printing.");
        if (request.Duplex) warnings.Add("Duplex was requested using PDF print settings, but final support depends on the printer driver.");
        if (string.Equals(request.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)) warnings.Add("Landscape orientation support depends on the PDF print tool and printer driver.");
        if (!string.IsNullOrWhiteSpace(request.PaperSize)) warnings.Add("Paper size support depends on the PDF print tool and printer driver.");
        return warnings;
    }
}

public static class PdfPrintTool
{
    private static readonly string[] DefaultPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SumatraPDF", "SumatraPDF.exe"),
        @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
        @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe"
    ];

    public const string UnavailableMessage =
        "PDF and Office printing require SumatraPDF. Install it or set its executable path in Settings.";

    public static string? ResolvePath() =>
        ResolvePath(new AgentSettingsStore().Load().PdfToolPath ?? Environment.GetEnvironmentVariable("PRINTR_PDF_COMMAND"), File.Exists, GetPathEnvironment());

    public static ConversionBackendStatus GetStatus()
    {
        var path = ResolvePath();
        return path is null
            ? new ConversionBackendStatus(false, Enabled: false, Message: UnavailableMessage)
            : new ConversionBackendStatus(true, path);
    }

    internal static string? ResolvePath(string? configuredPath, Func<string, bool> fileExists, string? pathEnvironment)
    {
        foreach (var candidate in new[] { configuredPath }.Concat(DefaultPaths).Concat(FindOnPath("SumatraPDF.exe", pathEnvironment)))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> FindOnPath(string fileName, string? pathEnvironment)
    {
        if (string.IsNullOrWhiteSpace(pathEnvironment))
        {
            yield break;
        }

        foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, fileName);
        }
    }

    private static string? GetPathEnvironment() => Environment.GetEnvironmentVariable("PATH");
}
