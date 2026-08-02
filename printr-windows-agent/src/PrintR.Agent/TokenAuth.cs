using System.Security.Cryptography;
using System.Text;

namespace PrintR.Agent;

public static class TokenAuth
{
    public static bool IsAuthorized(string expectedToken, string? suppliedToken)
    {
        expectedToken = expectedToken.Trim();
        suppliedToken = suppliedToken?.Trim();

        if (string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(expectedToken);
        var supplied = Encoding.UTF8.GetBytes(suppliedToken);
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
