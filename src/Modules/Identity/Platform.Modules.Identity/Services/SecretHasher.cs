using System.Security.Cryptography;
using System.Text;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// SHA-256 for high-entropy secrets (refresh tokens, API keys, recovery codes). These are random
/// 256-bit values, so a fast hash is appropriate; passwords use the slow PBKDF2 hasher instead.
/// </summary>
internal static class SecretHasher
{
    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static string NewSecret(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
