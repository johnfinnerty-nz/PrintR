using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PrintR.Agent;

namespace PrintR.Agent.Tests;

public sealed class TlsCertificateStoreTests
{
    [Fact]
    public void Fingerprint_Is_Stable_Sha256_For_A_Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=PrintR Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        var fingerprint = TlsCertificateStore.GetFingerprint(certificate);

        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, TlsCertificateStore.GetFingerprint(certificate));
        Assert.Matches("^[0-9A-F]+$", fingerprint);
    }
}
