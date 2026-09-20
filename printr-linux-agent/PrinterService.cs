namespace PrintR.Agent;

public sealed class PrinterService(AgentSettingsStore settingsStore, IDocumentConverter converter) : IPrinterService
{
    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        if (!File.Exists("/usr/bin/lpstat")) return [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var text = ProcessRunner.RunAsync("/usr/bin/lpstat", ["-p", "-d"], timeout.Token).GetAwaiter().GetResult();
            return ParsePrinters(text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        { ApiDiagnostics.RecordError("CUPS: " + ex.Message); return []; }
    }

    public static IReadOnlyList<PrinterInfo> ParsePrinters(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var defaultName = lines.FirstOrDefault(l => l.StartsWith("system default destination: "))?[28..].Trim();
        return lines.Where(l => l.StartsWith("printer ")).Select(line =>
        {
            var name = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            return new PrinterInfo(name, name == defaultName, line.Contains("disabled") ? "Disabled" : line.Contains("printing") ? "Printing" : "Ready");
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> PrintAsync(string path, PrintRequest request, CancellationToken cancellationToken, Action<JobStatus, string>? updateStatus = null)
    {
        if (!PrintRequestValidation.IsPageRangeValid(request.PageRange)) throw new ArgumentException("Invalid page range.");
        if (settingsStore.Load().MockPrintMode || Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT") == "true")
            return ["Mock print mode: job accepted but not sent to a printer."];
        if (!File.Exists("/usr/bin/lp")) throw new InvalidOperationException("Install cups-client and configure a CUPS printer first.");
        if (FileValidation.RequiresConversion(path))
        {
            updateStatus?.Invoke(JobStatus.Converting, "Converting document to PDF");
            var result = await converter.ConvertAsync(path, Path.GetDirectoryName(path)!, "pdf", cancellationToken);
            if (!result.Success || result.OutputPath is null) throw new InvalidOperationException(result.Message);
            path = result.OutputPath;
            updateStatus?.Invoke(JobStatus.Converted, "Document converted to PDF");
        }
        updateStatus?.Invoke(JobStatus.Printing, "Submitting to CUPS");
        var receipt = await ProcessRunner.RunAsync("/usr/bin/lp", BuildArguments(path, request), cancellationToken);
        return [receipt, "CUPS accepted the job. Physical completion depends on the printer."];
    }

    public static IReadOnlyList<string> BuildArguments(string path, PrintRequest request)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.PrinterName)) args.AddRange(["-d", request.PrinterName]);
        args.AddRange(["-n", Math.Clamp(request.Copies, 1, 99).ToString(), "-o", "fit-to-page", "-o", "media=" + (request.PaperSize == "letter" ? "Letter" : "A4"),
            "-o", "orientation-requested=" + (request.Orientation == "landscape" ? "4" : "3"),
            "-o", "print-color-mode=" + (request.ColorMode == "bw" ? "monochrome" : "color"),
            "-o", "sides=" + (request.DuplexMode.ToLowerInvariant() switch { "shortedge" => "two-sided-short-edge", "longedge" => "two-sided-long-edge", _ => request.Duplex ? "two-sided-long-edge" : "one-sided" })]);
        if (!string.IsNullOrWhiteSpace(request.PageRange)) args.AddRange(["-o", "page-ranges=" + request.PageRange.Replace(" ", "")]);
        args.Add("--");
        args.Add(Path.GetFullPath(path));
        return args;
    }
}

public static class PdfPrintTool
{
    public static ConversionBackendStatus GetStatus() => File.Exists("/usr/bin/lp")
        ? new(true, "/usr/bin/lp", Message: "CUPS client detected. Configure a printer with lpadmin or system printer settings.")
        : new(false, Message: "Install cups-client and configure a CUPS printer.");
}
