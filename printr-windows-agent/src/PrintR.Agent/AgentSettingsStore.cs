using System.Security.Cryptography;
using System.Text.Json;

namespace PrintR.Agent;

public sealed class AgentSettingsStore
{
    private readonly string _settingsPath;
    private readonly object _gate = new();

    public AgentSettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintR Agent");
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

    private static AgentSettings Normalize(AgentSettings settings) =>
        settings with
        {
            InstanceId = string.IsNullOrWhiteSpace(settings.InstanceId) ? Guid.NewGuid().ToString() : settings.InstanceId,
            FriendlyName = string.IsNullOrWhiteSpace(settings.FriendlyName) ? Environment.MachineName : settings.FriendlyName
        };

    private void Save(AgentSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).TrimEnd('=');
    }
}
