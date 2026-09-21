using System.Security.Cryptography;

namespace Wms.Infrastructure.ApiClients;

public sealed class ApiClientSecretHasher
{
    private const string Format = "wms-pbkdf2-v1";
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            secret,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
        return string.Join(
            '$',
            Format,
            Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(derived));
    }

    public static bool Verify(string secret, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$', StringSplitOptions.None);
        if (parts.Length != 4 || !string.Equals(parts[0], Format, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                secret,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
