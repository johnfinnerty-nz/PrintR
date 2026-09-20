using Microsoft.AspNetCore.Http.Features;
using PrintR.Agent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.RateLimiting;
#if WINDOWS
using System.Windows.Forms;
#endif

var settingsStore = new AgentSettingsStore();
var settings = settingsStore.Load();
var tlsCertificate = new TlsCertificateStore(settingsStore).LoadOrCreate();
if (args.Contains("--pairing")) { Console.WriteLine(new PairingService(settingsStore, tlsCertificate).CreatePayloadJson()); return; }
if (args.Contains("--status"))
{
    Console.WriteLine(JsonSerializer.Serialize(new { app = "PrintR Agent", version = AppInfo.Version, platform = Environment.OSVersion.Platform.ToString(), name = settings.FriendlyName, port = settings.Port, formats = FileValidation.SupportedExtensions, pdfPrintTool = PdfPrintTool.GetStatus(), configDirectory = AgentPaths.ConfigDirectory }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Contains("--help")) { Console.WriteLine("PrintR Agent: run without arguments to start. --headless: no desktop window. --status: show configuration. --pairing: print private pairing JSON. PRINTR_DATA_DIR: isolated configuration/data directory."); return; }

var instanceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AgentPaths.ConfigDirectory)))[..16];
using var singleInstanceMutex = new Mutex(false, "PrintR.Agent." + instanceKey);
bool isFirstInstance;
try { isFirstInstance = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(5)); }
catch (AbandonedMutexException) { isFirstInstance = true; }
if (!isFirstInstance)
{
#if WINDOWS
    if (!args.Contains("--headless")) MessageBox.Show("PrintR Agent is already running. Check the system tray for the PrintR icon.", "PrintR Agent");
    else Console.Error.WriteLine("PrintR Agent is already running for this configuration directory.");
#else
    Console.Error.WriteLine("PrintR Agent is already running for this configuration directory.");
#endif
    Environment.ExitCode = 1;
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = FileValidation.MaxUploadBytes + 1024 * 1024;
    options.Listen(IPAddress.Loopback, settings.Port, listen => listen.UseHttps(tlsCertificate.Certificate));
    foreach (var address in args.Contains("--loopback") ? [] : GetPrivateListenAddresses())
    {
        options.Listen(address, settings.Port, listen => listen.UseHttps(tlsCertificate.Certificate));
    }
});
builder.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = FileValidation.MaxUploadBytes; o.ValueLengthLimit = 4096; });
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton(settingsStore);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(tlsCertificate);
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<PairingService>();
if (!args.Contains("--loopback")) builder.Services.AddHostedService<DiscoveryService>();
builder.Services.AddSingleton<LibreOfficeDocumentConverter>();
builder.Services.AddSingleton<WordComDocumentConverter>();
builder.Services.AddSingleton<IDocumentConverter>(sp =>
    new CompositeDocumentConverter([
        sp.GetRequiredService<LibreOfficeDocumentConverter>(),
        sp.GetRequiredService<WordComDocumentConverter>()
    ]));
builder.Services.AddSingleton<IConversionBackendHealth>(sp => (CompositeDocumentConverter)sp.GetRequiredService<IDocumentConverter>());
builder.Services.AddSingleton<IPrinterService, PrinterService>();
builder.Services.AddSingleton<PrintQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrintQueue>());
builder.Services.AddLogging(o => o.AddConsole());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("uploads", _ => RateLimitPartition.GetConcurrencyLimiter("uploads", _ =>
        new ConcurrencyLimiterOptions { PermitLimit = 2, QueueLimit = 0 }));
});

var app = builder.Build();
app.UseRateLimiter();
var spoolDir = AgentPaths.SpoolDirectory;
Directory.CreateDirectory(spoolDir);

app.MapGet("/health", (IConversionBackendHealth conversionHealth, TlsCertificateInfo certificate) => Results.Json(new
{
    app = "PrintR Agent",
    status = "ok",
    version = AppInfo.Version,
    platform = OperatingSystem.IsWindows() ? "windows" : "linux",
    maxUploadMegabytes = settingsStore.Load().MaxUploadMegabytes,
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

app.MapGet("/diagnostics", (AgentSettingsStore store, IPrinterService printers, IDocumentConverter converter, JobStore jobs) =>
{
    var current = store.Load();
    var printerList = printers.GetPrinters();
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
        docxConverterAvailable = converter.CanConvert("document.docx", "pdf"),
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

app.MapGet("/jobs", (JobStore jobs) => Results.Json(jobs.Recent().Select(ToJobResponse)));

app.MapPost("/print", async (HttpRequest request, JobStore jobs, IPrinterService printerService, IDocumentConverter converter, PrintQueue queue, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form data." });
    }

    IFormCollection form;
    try { form = await request.ReadFormAsync(cancellationToken); }
    catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
    { return Results.BadRequest(new { error = "Invalid or oversized multipart upload. Maximum file size is 100 MB." }); }
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

    var currentSettings = settingsStore.Load();
    if (file.Length > currentSettings.MaxUploadMegabytes * 1024L * 1024L)
        return Results.Json(new { error = $"File exceeds this agent's {currentSettings.MaxUploadMegabytes} MB limit." }, statusCode: 413);
    if (!PrintRequestValidation.IsPageRangeValid(form["pageRange"]))
        return Results.BadRequest(new { error = "Invalid page range. Use page numbers such as 1-3,7." });
    var mock = currentSettings.MockPrintMode || Environment.GetEnvironmentVariable("PRINTR_MOCK_PRINT") == "true";
    if (!mock && FileValidation.RequiresConversion(safeName) && !converter.CanConvert(safeName, "pdf"))
        return Results.Json(new { error = "This document needs LibreOffice. Install it on the computer and restart the agent." }, statusCode: 422);
    if (!mock && (FileValidation.RequiresConversion(safeName) || Path.GetExtension(safeName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) && !PdfPrintTool.GetStatus().Available)
        return Results.Json(new { error = PdfPrintTool.GetStatus().Message }, statusCode: 422);

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
    try
    {
        await using (var stream = File.Create(storedPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }
        var content = FileValidation.ValidateContent(storedPath);
        if (!content.Ok)
        {
            jobs.Update(job.JobId, JobStatus.Failed, content.Message);
            Directory.Delete(jobDir, true);
            return Results.BadRequest(new { error = content.Message });
        }
        if (!queue.TryEnqueue(new QueuedPrint(job.JobId, storedPath, requestModel)))
        {
            jobs.Update(job.JobId, JobStatus.Failed, "Print queue is full. Try again shortly.");
            Directory.Delete(jobDir, true);
            return Results.Json(new { error = "Print queue is full. Try again shortly." }, statusCode: 503);
        }
    }
    catch (Exception ex) when (ex is IOException or OperationCanceledException)
    {
        jobs.Update(job.JobId, JobStatus.Failed, "Upload interrupted before it could be queued.");
        if (Directory.Exists(jobDir)) Directory.Delete(jobDir, true);
        throw;
    }

    var acceptedMessage = string.Equals(fileType, "docx", StringComparison.OrdinalIgnoreCase)
        ? "DOCX print job accepted"
        : "Print job accepted";
    return Results.Json(new { jobId = job.JobId, status = "queued", message = acceptedMessage }, statusCode: 202);
}).RequireRateLimiting("uploads");

try { await app.StartAsync(); }
catch (Exception ex)
{
    Console.Error.WriteLine($"PrintR could not start: {ex.Message}");
#if WINDOWS
    if (!args.Contains("--headless")) MessageBox.Show($"PrintR could not start. Check that port {settings.Port} is available.\n\n{ex.Message}", "PrintR Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
    Environment.ExitCode = 1;
    return;
}
#if WINDOWS
if (!args.Contains("--headless"))
{
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
}
#else
Console.WriteLine($"PrintR {AppInfo.Version} is ready on port {settings.Port}. Use --pairing locally to retrieve pairing details.");
#endif
await app.WaitForShutdownAsync();

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
