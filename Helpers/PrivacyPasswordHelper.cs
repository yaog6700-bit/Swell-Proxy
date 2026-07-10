using System;
using System.Security.Cryptography;
using System.Text;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Stores privacy-mode unlock credentials as salted PBKDF2 hashes (never plaintext).
    /// Format: v1$&lt;base64-salt&gt;$&lt;base64-hash&gt;
    /// Legacy plaintext values are accepted for verify and can be migrated via EnsureHashed.
    /// </summary>
    public static class PrivacyPasswordHelper
    {
        private const string Prefix = "v1$";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static bool IsHashed(string? stored)
            => !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Hash a new password for durable storage.</summary>
        public static string Hash(string password)
        {
            ArgumentException.ThrowIfNullOrEmpty(password);

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return Prefix
                + Convert.ToBase64String(salt)
                + "$"
                + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// If <paramref name="stored"/> is legacy plaintext, re-hash it; otherwise return as-is.
        /// </summary>
        public static string EnsureHashed(string stored)
        {
            if (string.IsNullOrEmpty(stored) || IsHashed(stored))
                return stored;
            return Hash(stored);
        }

        /// <summary>Verify a user-entered password against a stored hash or legacy plaintext.</summary>
        public static bool Verify(string password, string? stored)
        {
            if (string.IsNullOrEmpty(stored) || password is null)
                return false;

            if (!IsHashed(stored))
                return string.Equals(stored, password, StringComparison.Ordinal);

            // v1$salt$hash
            var parts = stored.Split('$');
            if (parts.Length != 3 || parts[0] != "v1")
                return false;

            try
            {
                var salt = Convert.FromBase64String(parts[1]);
                var expected = Convert.FromBase64String(parts[2]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}
