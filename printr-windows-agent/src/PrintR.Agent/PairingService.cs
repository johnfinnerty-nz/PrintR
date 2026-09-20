using System.Net;
using System.Net.Sockets;
using System.Text.Json;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif
using QRCoder;

namespace PrintR.Agent;

public sealed class PairingService(AgentSettingsStore settingsStore, TlsCertificateInfo tlsCertificate)
{
    public PairingPayload CreatePayload(IPAddress? preferredAddress = null)
    {
        var settings = settingsStore.Load();
        var ip = preferredAddress ?? NetworkInfo.GetPrivateIps().FirstOrDefault() ?? IPAddress.Loopback;
        return new PairingPayload(
            "PrintR Agent",
            AppInfo.Version,
            settings.InstanceId!,
            settings.FriendlyName ?? Environment.MachineName,
            Dns.GetHostName(),
            ip.ToString(),
            settings.Port,
            settings.Token,
            "https",
            tlsCertificate.Fingerprint);
    }

    public string CreatePayloadJson(IPAddress? preferredAddress = null) =>
        JsonSerializer.Serialize(CreatePayload(preferredAddress), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

#if WINDOWS
    public Bitmap CreateQrBitmap(IPAddress? preferredAddress = null)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(CreatePayloadJson(preferredAddress), QRCodeGenerator.ECCLevel.Q);
        using var qr = new QRCode(data);
        return qr.GetGraphic(8);
    }
#endif
}

public static class NetworkInfo
{
    public static IReadOnlyList<IPAddress> GetPrivateIps() =>
        Dns.GetHostAddresses(Dns.GetHostName())
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Where(NetworkGuards.IsPrivateOrLoopback)
            .Where(ip => !IPAddress.IsLoopback(ip))
            .Distinct()
            .ToList();

    public static IReadOnlyList<string> GetBoundInterfaces(int port, string scheme = "https") =>
        GetPrivateIps().Select(ip => $"{scheme}://{ip}:{port}").Prepend($"{scheme}://127.0.0.1:{port}").ToList();
}

public sealed class DiscoveryService(
    AgentSettingsStore settingsStore,
    TlsCertificateInfo tlsCertificate,
    ILogger<DiscoveryService> logger) : BackgroundService
{
    public const int DiscoveryPort = 8788;
    public const string Probe = "PRINTR_DISCOVER_V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            logger.LogWarning(ex, "PrintR UDP discovery port {Port} is already in use; continuing without LAN discovery listener.", DiscoveryPort);
            return;
        }

        using (udp)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var received = await udp.ReceiveAsync(stoppingToken);
                    if (!NetworkGuards.IsPrivateOrLoopback(received.RemoteEndPoint.Address)) continue;
                    var text = System.Text.Encoding.UTF8.GetString(received.Buffer);
                    if (!string.Equals(text, Probe, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var settings = settingsStore.Load();
                    var response = JsonSerializer.Serialize(new
                    {
                        app = "PrintR Agent",
                        version = AppInfo.Version,
                        instanceId = settings.InstanceId,
                        computerName = settings.FriendlyName ?? Environment.MachineName,
                        host = NetworkInfo.GetPrivateIps().FirstOrDefault()?.ToString() ?? Dns.GetHostName(),
                        port = settings.Port,
                        scheme = "https",
                        tlsFingerprint = tlsCertificate.Fingerprint,
                        requiresPairing = true
                    });
                    var bytes = System.Text.Encoding.UTF8.GetBytes(response);
                    await udp.SendAsync(bytes, bytes.Length, received.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Discovery listener error");
                }
            }
        }
    }
}

public static class FirewallDiagnostics
{
    public static string GetWarning()
    {
        return OperatingSystem.IsWindows() ? "Allow PrintR Agent through Windows Defender Firewall on Private networks." : "Allow TCP 8787 (or your configured port) and UDP 8788 from your trusted LAN only.";
    }
}

#if WINDOWS
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrintR Agent";

    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled)
        {
            key.SetValue(ValueName, '"' + Application.ExecutablePath + '"');
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
#endif
