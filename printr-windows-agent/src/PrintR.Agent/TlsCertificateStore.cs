using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PrintR.Agent;

public sealed record TlsCertificateInfo(X509Certificate2 Certificate, string Fingerprint);

public sealed class TlsCertificateStore
{
    private const string CertificateFileName = "tls-certificate.pfx";
    private readonly AgentSettingsStore _settingsStore;
    private readonly string _certificatePath;

    public TlsCertificateStore(AgentSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintR Agent");
        Directory.CreateDirectory(settingsDirectory);
        _certificatePath = Path.Combine(settingsDirectory, CertificateFileName);
    }

    public TlsCertificateInfo LoadOrCreate()
    {
        var settings = _settingsStore.Load();
        if (File.Exists(_certificatePath) && !string.IsNullOrWhiteSpace(settings.TlsCertificatePassword))
        {
            try
            {
                return ToInfo(LoadCertificate(File.ReadAllBytes(_certificatePath), settings.TlsCertificatePassword));
            }
            catch (CryptographicException)
            {
                // A corrupt local certificate cannot be trusted. A replacement requires Android devices to pair again.
            }
        }

        var password = CreateSecret();
        using var created = CreateCertificate();
        File.WriteAllBytes(_certificatePath, created.Export(X509ContentType.Pfx, password));
        _settingsStore.UpdateTlsCertificatePassword(password);
        return ToInfo(LoadCertificate(File.ReadAllBytes(_certificatePath), password));
    }

    internal static string GetFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static TlsCertificateInfo ToInfo(X509Certificate2 certificate) =>
        new(certificate, GetFingerprint(certificate));

    private static X509Certificate2 LoadCertificate(byte[] bytes, string password) =>
        new(bytes, password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=PrintR Agent", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
            critical: false));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddDnsName(Dns.GetHostName());
        foreach (var address in NetworkInfo.GetPrivateIps())
        {
            subjectAlternativeNames.AddIpAddress(address);
        }
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static string CreateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
