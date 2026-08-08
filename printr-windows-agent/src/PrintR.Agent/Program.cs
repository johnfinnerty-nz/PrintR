using Microsoft.AspNetCore.Http.Features;
using PrintR.Agent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms;

using var singleInstanceMutex = new Mutex(true, "PrintR.Agent.SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    MessageBox.Show("PrintR Agent is already running. Check the system tray for the PrintR icon.", "PrintR Agent");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var settingsStore = new AgentSettingsStore();
var settings = settingsStore.Load();
var tlsCertificate = new TlsCertificateStore(settingsStore).LoadOrCreate();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, settings.Port, listen => listen.UseHttps(tlsCertificate.Certificate));
    foreach (var address in GetPrivateListenAddresses())
    {
        options.Listen(address, settings.Port, listen => listen.UseHttps(tlsCertificate.Certificate));
    }
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = FileValidation.MaxUploadBytes);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(tlsCertificate);
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddHostedService<DiscoveryService>();
builder.Services.AddSingleton<LibreOfficeDocumentConverter>();
builder.Services.AddSingleton<WordComDocumentConverter>();
builder.Services.AddSingleton<IDocumentConverter>(sp =>
    new CompositeDocumentConverter([
        sp.GetRequiredService<LibreOfficeDocumentConverter>(),
        sp.GetRequiredService<WordComDocumentConverter>()
    ]));
builder.Services.AddSingleton<IConversionBackendHealth>(sp => (CompositeDocumentConverter)sp.GetRequiredService<IDocumentConverter>());
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddLogging(o => o.AddConsole());

var app = builder.Build();
var spoolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintR Agent", "Spool");
Directory.CreateDirectory(spoolDir);

app.MapGet("/health", (IConversionBackendHealth conversionHealth, TlsCertificateInfo certificate) => Results.Json(new
{
    app = "PrintR Agent",
    status = "ok",
    version = AppInfo.Version,
    scheme = "https",
    tlsFingerprint = certificate.Fingerprint,
    supportedFormats = FileValidation.SupportedExtensions.Select(e => e.TrimStart('.')),
    pdfPrintTool = PdfPrintTool.GetStatus(),
    conversionBackends = conversionHealth.GetHealth()
}));

app.MapGet("/discovery-info", (AgentSettingsStore store, TlsCertificateInfo certificate) =>
{
    var current = store.Load();
    return Results.Json(new
    {
        app = "PrintR Agent",
        version = AppInfo.Version,
        instanceId = current.InstanceId,
        computerName = current.FriendlyName ?? Environment.MachineName,
        scheme = "https",
        tlsFingerprint = certificate.Fingerprint,
        requiresPairing = true
    });
});

app.MapPost("/pair/test", async (HttpRequest request, AgentSettingsStore store) =>
{
    PairTestRequest? body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<PairTestRequest>(
            request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            request.HttpContext.RequestAborted);
    }
    catch (JsonException)
    {
        return Results.Json(new { success = false, message = "Invalid pairing request." }, statusCode: StatusCodes.Status400BadRequest);
    }

    var current = store.Load();
    if (body is null || !TokenAuth.IsAuthorized(current.Token, body.Token))
    {
        return Results.Json(new { success = false, message = "Invalid pairing token" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Json(new
    {
        success = true,
        instanceId = current.InstanceId,
        computerName = current.FriendlyName ?? Environment.MachineName,
        message = "Pairing successful"
    });
});

app.Use(async (context, next) =>
{
    ApiDiagnostics.RecordConnection(context.Connection.RemoteIpAddress?.ToString());
    if (!NetworkGuards.IsPrivateOrLoopback(context.Connection.RemoteIpAddress))
    {
        ApiDiagnostics.RecordError("Rejected non-private network client.");
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "PrintR Agent only accepts local/private network clients." });
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health" ||
        context.Request.Path == "/discovery-info" ||
        context.Request.Path == "/pair/test")
    {
        await next();
        return;
    }

    var expected = context.RequestServices.GetRequiredService<AgentSettingsStore>().Load().Token;
    if (!context.Request.Headers.TryGetValue("X-PrintR-Token", out var token) || !TokenAuth.IsAuthorized(expected, token.ToString()))
    {
        ApiDiagnostics.RecordError("Rejected request with missing or invalid token.");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid pairing token." });
        return;
    }

    await next();
});

app.MapGet("/printers", (IPrinterService printers) =>
{
    var list = printers.GetPrinters();
    return Results.Json(new
    {
        defaultPrinter = list.FirstOrDefault(p => p.IsDefault)?.Name,
        printers = list
    });
});

app.MapGet("/diagnostics", (AgentSettingsStore store, IPrinterService printers, IConversionBackendHealth conversionHealth, JobStore jobs) =>
{
    var current = store.Load();
    var printerList = printers.GetPrinters();
    var conversionJson = JsonSerializer.Serialize(conversionHealth.GetHealth(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    return Results.Json(new
    {
        service = "running",
        port = current.Port,
        boundInterfaces = NetworkInfo.GetBoundInterfaces(current.Port),
        localIps = NetworkInfo.GetPrivateIps().Select(ip => ip.ToString()),
        firewallWarning = FirewallDiagnostics.GetWarning(),
        defaultPrinter = printerList.FirstOrDefault(p => p.IsDefault)?.Name,
        supportedFormats = FileValidation.SupportedExtensions.Select(e => e.TrimStart('.')),
        pdfPrintTool = PdfPrintTool.GetStatus(),
        docxConverterAvailable = conversionJson.Contains("\"available\":true", StringComparison.OrdinalIgnoreCase) ||
                                 conversionJson.Contains("\"available\": true", StringComparison.OrdinalIgnoreCase),
        mockPrintMode = current.MockPrintMode || string.Equals(Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT"), "true", StringComparison.OrdinalIgnoreCase),
        recentErrors = jobs.Recent().Where(j => j.Status == JobStatus.Failed).Take(10).Select(j => new { j.JobId, j.FileName, j.ErrorMessage }),
        lastAndroidConnectionAttempt = ApiDiagnostics.LastConnectionAttempt,
        lastApiError = ApiDiagnostics.LastApiError
    });
});

app.MapGet("/jobs/{id:guid}", (Guid id, JobStore jobs) =>
{
    var job = jobs.Get(id);
    return job is null
        ? Results.NotFound(new { error = "Job not found." })
        : Results.Json(ToJobResponse(job));
});

app.MapPost("/print", async (HttpRequest request, JobStore jobs, IPrinterService printerService, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null)
    {
        return Results.BadRequest(new { error = "Missing multipart file field named 'file'." });
    }

    var safeName = FileValidation.SanitizeFileName(file.FileName);
    var validation = FileValidation.Validate(safeName, file.ContentType, file.Length);
    if (!validation.Ok)
    {
        return Results.BadRequest(new { error = validation.Message });
    }

    var requestModel = new PrintRequest(
        EmptyToNull(form["printerName"]),
        int.TryParse(form["copies"], out var copies) ? Math.Clamp(copies, 1, 99) : 1,
        form["colorMode"].ToString(),
        ParseDuplex(form["duplexMode"].ToString(), form["duplex"].ToString()),
        EmptyToNull(form["pageRange"]),
        NormalizeDuplexMode(form["duplexMode"].ToString(), form["duplex"].ToString()),
        NormalizeOrientation(form["orientation"].ToString()),
        NormalizePaperSize(form["paperSize"].ToString()));

    var fileType = Path.GetExtension(safeName).TrimStart('.').ToLowerInvariant();
    var job = jobs.AddQueued(requestModel.PrinterName, safeName, fileType);
    var jobDir = Path.Combine(spoolDir, job.JobId.ToString());
    Directory.CreateDirectory(jobDir);
    var storedPath = Path.Combine(jobDir, safeName);
    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream, cancellationToken);
    }

    _ = Task.Run(async () =>
    {
        var logger = loggerFactory.CreateLogger("PrintJob");
        try
        {
            if (!string.Equals(fileType, "docx", StringComparison.OrdinalIgnoreCase))
            {
                jobs.Update(job.JobId, JobStatus.Printing, "Printing");
            }
            var warnings = await printerService.PrintAsync(
                storedPath,
                requestModel,
                CancellationToken.None,
                (status, message) => jobs.Update(job.JobId, status, message));
            jobs.Update(job.JobId, JobStatus.Completed, "Printed successfully", warnings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Print job {JobId} failed", job.JobId);
            ApiDiagnostics.RecordError($"Print job {job.JobId} failed: {ex.Message}");
            jobs.Update(job.JobId, JobStatus.Failed, ex.Message);
        }
        finally
        {
            if (!SpoolCleanup.ShouldKeepFiles(settings))
            {
                try { SpoolCleanup.CleanupJobFolder(jobDir, settings); } catch (Exception ex) { logger.LogWarning(ex, "Could not delete spool folder {Path}", jobDir); }
            }
        }
    });

    var acceptedMessage = string.Equals(fileType, "docx", StringComparison.OrdinalIgnoreCase)
        ? "DOCX print job accepted"
        : "Print job accepted";
    return Results.Json(new { jobId = job.JobId, status = "queued", message = acceptedMessage });
});

var webTask = app.RunAsync();
var uiThread = new Thread(() =>
{
    ApplicationConfiguration.Initialize();
    var form = new AgentForm(
        settingsStore,
        settings,
        app.Services.GetRequiredService<IPrinterService>(),
        app.Services.GetRequiredService<JobStore>(),
        app.Services.GetRequiredService<IConversionBackendHealth>(),
        app.Services.GetRequiredService<PairingService>(),
        app.Services.GetRequiredService<TlsCertificateInfo>(),
        () => _ = app.StopAsync());
    Application.Run(form);
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
await webTask;

static string? EmptyToNull(object? value)
{
    var text = value?.ToString();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static string NormalizeDuplexMode(string? value, string? legacyDuplex)
{
    var normalized = value?.Trim().ToLowerInvariant();
    if (normalized is "longedge" or "shortedge" or "none") return normalized;
    return bool.TryParse(legacyDuplex, out var duplex) && duplex ? "longedge" : "none";
}

static bool ParseDuplex(string? value, string? legacyDuplex) => NormalizeDuplexMode(value, legacyDuplex) != "none";

static string NormalizeOrientation(string? value) =>
    string.Equals(value?.Trim(), "landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait";

static string NormalizePaperSize(string? value) =>
    string.Equals(value?.Trim(), "letter", StringComparison.OrdinalIgnoreCase) ? "letter" : "a4";

static object ToJobResponse(PrintJob job) => new
{
    jobId = job.JobId,
    status = job.Status.ToString().ToLowerInvariant(),
    message = job.Message,
    fileName = job.FileName,
    fileType = job.FileType,
    printer = job.PrinterName,
    submittedAt = job.CreatedAt,
    completedAt = job.CompletedAt,
    errorMessage = job.ErrorMessage,
    warnings = job.Warnings
};

static IEnumerable<IPAddress> GetPrivateListenAddresses()
{
    return Dns.GetHostAddresses(Dns.GetHostName())
        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
        .Where(NetworkGuards.IsPrivateOrLoopback)
        .Where(ip => !IPAddress.IsLoopback(ip))
        .Distinct();
}

public sealed record PairTestRequest(string Token);
