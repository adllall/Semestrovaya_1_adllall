using System.Security.Cryptography;

namespace Semestrovaya.Security;

public static class Passwords
{
    private const int Iterations = 600_000;
    private static readonly byte[] DummySalt = RandomNumberGenerator.GetBytes(16);

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return string.Join('$', "pbkdf2-sha256", Iterations.ToString(),
            Convert.ToBase64String(salt), Convert.ToBase64String(Derive(password, salt, Iterations)));
    }

    public static bool Verify(string password, string? stored)
    {
        string[]? parts = stored?.Split('$');
        if (parts is not [ "pbkdf2-sha256", var count, var encodedSalt, var encodedHash ] ||
            !int.TryParse(count, out int iterations) || iterations is < 100_000 or > 1_000_000)
        {
            _ = Derive(password, DummySalt, Iterations);
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(encodedSalt);
            byte[] hash = Convert.FromBase64String(encodedHash);
            return salt.Length == 16 && hash.Length == 32 &&
                CryptographicOperations.FixedTimeEquals(hash, Derive(password, salt, iterations));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
}
