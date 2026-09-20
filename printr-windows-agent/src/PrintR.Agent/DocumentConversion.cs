using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrintR.Agent;

public interface IDocumentConverter
{
    bool CanConvert(string inputPath, string outputFormat);
    Task<DocumentConversionResult> ConvertAsync(
        string inputPath,
        string outputDirectory,
        string outputFormat,
        CancellationToken cancellationToken);
}

public interface IConversionBackendHealth
{
    object GetHealth();
}

public sealed class CompositeDocumentConverter(IEnumerable<IDocumentConverter> converters) : IDocumentConverter, IConversionBackendHealth
{
    private readonly IReadOnlyList<IDocumentConverter> _converters = converters.ToList();

    public bool CanConvert(string inputPath, string outputFormat) =>
        _converters.Any(c => c.CanConvert(inputPath, outputFormat));

    public async Task<DocumentConversionResult> ConvertAsync(string inputPath, string outputDirectory, string outputFormat, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var converter in _converters)
        {
            if (converter.CanConvert(inputPath, outputFormat))
            {
                var result = await converter.ConvertAsync(inputPath, outputDirectory, outputFormat, cancellationToken);
                if (result.Success)
                {
                    return result;
                }

                failures.Add(result.Message);
            }
        }

        return new DocumentConversionResult(
            false,
            null,
            failures.Count == 0
                ? "Office document printing requires LibreOffice. Install LibreOffice or configure a converter path."
                : string.Join(" ", failures),
            []);
    }

    public object GetHealth()
    {
        var libreOffice = _converters.OfType<LibreOfficeDocumentConverter>().FirstOrDefault();
        var wordCom = _converters.OfType<WordComDocumentConverter>().FirstOrDefault();
        return new
        {
            libreOffice = libreOffice?.Status ?? new ConversionBackendStatus(false, Message: "LibreOffice converter not registered."),
            wordCom = wordCom?.Status ?? new ConversionBackendStatus(false, Enabled: false, Message: "Word COM converter not registered.")
        };
    }
}

public sealed class LibreOfficeDocumentConverter : IDocumentConverter
{
    private static readonly string[] DefaultPaths =
    [
        @"C:\Program Files\LibreOffice\program\soffice.exe",
        @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        ,"/usr/bin/libreoffice", "/usr/bin/soffice", "/usr/local/bin/libreoffice"
    ];

    private readonly ILogger<LibreOfficeDocumentConverter> _logger;
    private readonly string? _sofficePath;
    private readonly TimeSpan _timeout;

    public LibreOfficeDocumentConverter(AgentSettings settings, ILogger<LibreOfficeDocumentConverter> logger)
        : this(settings, logger, File.Exists, TimeSpan.FromMinutes(2))
    {
    }

    internal LibreOfficeDocumentConverter(
        AgentSettings settings,
        ILogger<LibreOfficeDocumentConverter> logger,
        Func<string, bool> fileExists,
        TimeSpan timeout)
    {
        _logger = logger;
        _timeout = timeout;
        _sofficePath = ResolvePath(settings.LibreOfficePath, Environment.GetEnvironmentVariable("PRINTR_LIBREOFFICE_PATH"), fileExists);
        Status = _sofficePath is null
            ? new ConversionBackendStatus(false, Message: "LibreOffice was not detected. Install LibreOffice or configure PRINTR_LIBREOFFICE_PATH.")
            : new ConversionBackendStatus(true, _sofficePath);
    }

    public ConversionBackendStatus Status { get; }

    public bool CanConvert(string inputPath, string outputFormat) =>
        _sofficePath is not null &&
        FileValidation.RequiresConversion(inputPath) &&
        string.Equals(outputFormat, "pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<DocumentConversionResult> ConvertAsync(string inputPath, string outputDirectory, string outputFormat, CancellationToken cancellationToken)
    {
        if (!CanConvert(inputPath, outputFormat))
        {
            return new DocumentConversionResult(false, null, Status.Message ?? "LibreOffice cannot convert this file.", []);
        }

        Directory.CreateDirectory(outputDirectory);
        var expectedPdf = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        var start = new ProcessStartInfo
        {
            FileName = _sofficePath!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--headless");
        // A separate profile avoids attaching to an open office session. Disable macros in that profile.
        var profileDirectory = Path.Combine(outputDirectory, "lo-profile");
        Directory.CreateDirectory(Path.Combine(profileDirectory, "user"));
        AgentPaths.WritePrivateText(Path.Combine(profileDirectory, "user", "registrymodifications.xcu"),
            "<?xml version=\"1.0\"?><oor:items xmlns:oor=\"http://openoffice.org/2001/registry\"><item oor:path=\"/org.openoffice.Office.Common/Security/Scripting\"><prop oor:name=\"MacroSecurityLevel\" oor:op=\"fuse\"><value>3</value></prop></item></oor:items>");
        start.ArgumentList.Add("-env:UserInstallation=" + new Uri(Path.GetFullPath(profileDirectory) + Path.DirectorySeparatorChar).AbsoluteUri);
        start.ArgumentList.Add("--norestore");
        start.ArgumentList.Add("--convert-to");
        start.ArgumentList.Add("pdf");
        start.ArgumentList.Add("--outdir");
        start.ArgumentList.Add(outputDirectory);
        start.ArgumentList.Add(inputPath);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start LibreOffice.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("LibreOffice conversion failed with exit code {ExitCode}. Stderr: {Stderr}", process.ExitCode, TrimForLog(stderr));
                return new DocumentConversionResult(false, null, $"LibreOffice conversion failed with exit code {process.ExitCode}. {TrimForUser(stderr)}", []);
            }

            var missingOutput = ValidateExpectedOutput(expectedPdf, File.Exists);
            if (missingOutput is not null)
            {
                _logger.LogWarning("LibreOffice reported success but expected PDF was missing. Stdout: {Stdout}; Stderr: {Stderr}", TrimForLog(stdout), TrimForLog(stderr));
                return missingOutput;
            }

            return new DocumentConversionResult(true, expectedPdf, "Converted document to PDF", []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new DocumentConversionResult(false, null, "LibreOffice conversion timed out.", []);
        }
        catch (OperationCanceledException) { TryKill(process); throw; }
    }

    internal static string? ResolvePath(string? configuredPath, string? environmentPath, Func<string, bool> fileExists)
    {
        foreach (var candidate in new[] { configuredPath, environmentPath }.Concat(DefaultPaths))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static DocumentConversionResult? ValidateExpectedOutput(string expectedPdf, Func<string, bool> fileExists) =>
        fileExists(expectedPdf)
            ? null
            : new DocumentConversionResult(false, null, "DOCX conversion failed because the generated PDF was not found.", []);

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static string TrimForLog(string value) => value.Length > 2000 ? value[..2000] : value;

    private static string TrimForUser(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Check the PrintR Agent logs for details." : TrimForLog(value).ReplaceLineEndings(" ");
}

public sealed class WordComDocumentConverter : IDocumentConverter
{
    private readonly AgentSettings _settings;
    private readonly ILogger<WordComDocumentConverter> _logger;

    public WordComDocumentConverter(AgentSettings settings, ILogger<WordComDocumentConverter> logger)
    {
        _settings = settings;
        _logger = logger;
        var available = OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Word.Application") is not null;
        Status = new ConversionBackendStatus(available, Enabled: settings.WordComEnabled, Message: settings.WordComEnabled ? null : "Word COM conversion is disabled by default.");
    }

    public ConversionBackendStatus Status { get; }

    public bool CanConvert(string inputPath, string outputFormat) =>
        _settings.WordComEnabled &&
        Status.Available &&
        string.Equals(Path.GetExtension(inputPath), ".docx", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(outputFormat, "pdf", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentConversionResult> ConvertAsync(string inputPath, string outputDirectory, string outputFormat, CancellationToken cancellationToken)
    {
        if (!CanConvert(inputPath, outputFormat))
        {
            return Task.FromResult(new DocumentConversionResult(false, null, "Microsoft Word COM conversion is unavailable or disabled.", []));
        }

        return Task.Run(() =>
        {
            object? word = null;
            object? document = null;
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
                var type = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word is not installed.");
                word = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not start Microsoft Word.");
                dynamic app = word;
                app.Visible = false;
                app.DisplayAlerts = 0;
                try { app.AutomationSecurity = 3; } catch { }
                document = app.Documents.Open(inputPath, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                dynamic doc = document;
                doc.ExportAsFixedFormat(outputPath, 17);
                doc.Close(false);
                document = null;
                app.Quit(false);
                word = null;

                if (!File.Exists(outputPath))
                {
                    return new DocumentConversionResult(false, null, "Word conversion failed because the generated PDF was not found.", []);
                }

                return new DocumentConversionResult(true, outputPath, "Converted DOCX to PDF using Microsoft Word", ["Word COM conversion is enabled. Office automation can be brittle for unattended printing."]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Word COM conversion failed");
                return new DocumentConversionResult(false, null, "Microsoft Word conversion failed. Install LibreOffice or check Word COM configuration.", []);
            }
            finally
            {
                TryCloseCom(document, "Close", false);
                TryCloseCom(word, "Quit", false);
            }
        }, cancellationToken);
    }

    private static void TryCloseCom(object? comObject, string methodName, bool arg)
    {
        if (comObject is null) return;
        try
        {
            comObject.GetType().InvokeMember(methodName, System.Reflection.BindingFlags.InvokeMethod, null, comObject, [arg]);
        }
        catch { }
        finally
        {
            try { Marshal.FinalReleaseComObject(comObject); } catch { }
        }
    }
}
