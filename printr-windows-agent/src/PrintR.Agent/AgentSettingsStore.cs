using System.Security.Cryptography;
using System.Text.Json;

namespace PrintR.Agent;

public sealed class AgentSettingsStore
{
    private readonly string _settingsPath;
    private readonly object _gate = new();

    public AgentSettingsStore()
    {
        var dir = AgentPaths.ConfigDirectory;
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public AgentSettings Load()
    {
        lock (_gate)
        {
            if (File.Exists(_settingsPath))
            {
                var existing = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(_settingsPath));
                if (existing is not null && !string.IsNullOrWhiteSpace(existing.Token))
                {
                    var normalized = Normalize(existing);
                    if (normalized != existing) Save(normalized);
                    return normalized;
                }
            }

            var settings = Normalize(new AgentSettings(CreateToken(), 8787, false));
            Save(settings);
            return settings;
        }
    }

    public AgentSettings RotateToken()
    {
        lock (_gate)
        {
            var current = Load();
            var updated = current with { Token = CreateToken() };
            Save(updated);
            return updated;
        }
    }

    public AgentSettings UpdateLibreOfficePath(string? path)
    {
        lock (_gate)
        {
            var current = Load();
            var updated = current with { LibreOfficePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim() };
            Save(updated);
            return updated;
        }
    }

    public AgentSettings UpdateStartWithWindows(bool enabled)
    {
        lock (_gate)
        {
            var current = Load();
            var updated = current with { StartWithWindows = enabled };
            Save(updated);
            return updated;
        }
    }

    public AgentSettings UpdateMockPrintMode(bool enabled)
    {
        lock (_gate)
        {
            var current = Load();
            var updated = current with { MockPrintMode = enabled };
            Save(updated);
            return updated;
        }
    }

    public AgentSettings UpdateTlsCertificatePassword(string password)
    {
        lock (_gate)
        {
            var current = Load();
            var updated = current with { TlsCertificatePassword = password };
            Save(updated);
            return updated;
        }
    }

    public AgentSettings UpdatePreferences(string name, int port, string? libreOffice, string? pdfTool, int maxUpload, int timeout, bool keepFiles, bool mock)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Computer name must be 1 to 80 characters.");
        if (port is < 1024 or > 65535) throw new ArgumentException("Port must be between 1024 and 65535.");
        if (maxUpload is < 1 or > 100) throw new ArgumentException("Upload limit must be 1 to 100 MB.");
        if (timeout is < 30 or > 600) throw new ArgumentException("Job timeout must be 30 to 600 seconds.");
        foreach (var path in new[] { libreOffice, pdfTool })
            if (!string.IsNullOrWhiteSpace(path) && (!Path.IsPathFullyQualified(path) || !File.Exists(path)))
                throw new ArgumentException("Tool paths must point to an existing executable using an absolute path.");
        lock (_gate)
        {
            var updated = Load() with { FriendlyName = name.Trim(), Port = port, LibreOfficePath = string.IsNullOrWhiteSpace(libreOffice) ? null : libreOffice.Trim(), PdfToolPath = string.IsNullOrWhiteSpace(pdfTool) ? null : pdfTool.Trim(), MaxUploadMegabytes = maxUpload, JobTimeoutSeconds = timeout, DebugKeepSpoolFiles = keepFiles, MockPrintMode = mock };
            Save(updated);
            return updated;
        }
    }

    private static AgentSettings Normalize(AgentSettings settings) =>
        settings with
        {
            InstanceId = string.IsNullOrWhiteSpace(settings.InstanceId) ? Guid.NewGuid().ToString() : settings.InstanceId,
            FriendlyName = string.IsNullOrWhiteSpace(settings.FriendlyName) ? Environment.MachineName : settings.FriendlyName,
            Port = settings.Port is >= 1024 and <= 65535 ? settings.Port : 8787,
            MaxUploadMegabytes = Math.Clamp(settings.MaxUploadMegabytes, 1, 100),
            JobTimeoutSeconds = Math.Clamp(settings.JobTimeoutSeconds, 30, 600)
        };

    private void Save(AgentSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        AgentPaths.WritePrivateText(_settingsPath, json);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).TrimEnd('=');
    }
}
