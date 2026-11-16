using System;
using System.Security.Cryptography;
using System.Text;

namespace EMB_Production.Infrastructure
{
    /// <summary>
    /// PBKDF2 (HMAC-SHA256) password hashing using only built-in .NET Framework
    /// cryptography APIs (no third-party package). Stored format:
    ///   "{iterations}.{saltBase64}.{hashBase64}"
    ///
    /// Backward compatibility: existing accounts in the database still have their
    /// original PLAINTEXT password in the Password column (that is exactly the bug
    /// this class fixes going forward). Those values don't match the stored format
    /// above, so <see cref="Verify"/> detects that and falls back to a direct
    /// legacy string comparison. Callers (AuthController.Login / ChangePassword)
    /// are responsible for re-hashing and saving the password immediately after a
    /// successful legacy comparison ("lazy migration"), so the account is upgraded
    /// to a proper hash the next time it's used.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32; // SHA-256 output size
        private const int DefaultIterations = 100000;

        /// <summary>Hashes a plaintext password into the "{iterations}.{salt}.{hash}" format.</summary>
        public static string Hash(string password)
        {
            password = password ?? string.Empty;

            byte[] salt = new byte[SaltSizeBytes];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            byte[] hash = Pbkdf2Sha256(password, salt, DefaultIterations, HashSizeBytes);

            return string.Format(
                "{0}.{1}.{2}",
                DefaultIterations,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        /// <summary>
        /// True if <paramref name="stored"/> looks like a value produced by <see cref="Hash"/>
        /// (iterations.saltBase64.hashBase64), as opposed to a legacy plaintext password.
        /// </summary>
        public static bool IsHashed(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return false;

            var parts = stored.Split('.');
            if (parts.Length != 3)
                return false;

            int iterations;
            if (!int.TryParse(parts[0], out iterations) || iterations <= 0)
                return false;

            try
            {
                Convert.FromBase64String(parts[1]);
                Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies <paramref name="password"/> against <paramref name="stored"/>.
        /// If <paramref name="stored"/> is a recognized hash, performs a constant-time
        /// PBKDF2 comparison. Otherwise falls back to a direct comparison against the
        /// legacy plaintext value so existing accounts keep working until they are
        /// lazily migrated by the caller.
        /// </summary>
        public static bool Verify(string password, string stored)
        {
            password = password ?? string.Empty;

            if (string.IsNullOrEmpty(stored))
                return false;

            if (!IsHashed(stored))
            {
                // Legacy plaintext account.
                return string.Equals(password, stored, StringComparison.Ordinal);
            }

            var parts = stored.Split('.');

            int iterations;
            byte[] salt;
            byte[] expectedHash;
            try
            {
                iterations = int.Parse(parts[0]);
                salt = Convert.FromBase64String(parts[1]);
                expectedHash = Convert.FromBase64String(parts[2]);
            }
            catch
            {
                return false;
            }

            byte[] actualHash = Pbkdf2Sha256(password, salt, iterations, expectedHash.Length);
            return FixedTimeEquals(actualHash, expectedHash);
        }

        // ------------------------------------------------------------------
        // PBKDF2 (RFC 2898) implemented directly on top of HMACSHA256, which is
        // a built-in BCL primitive on every .NET Framework version — this avoids
        // depending on the newer Rfc2898DeriveBytes(..., HashAlgorithmName) overload
        // that may not be present on this exact target.
        // ------------------------------------------------------------------
        private static byte[] Pbkdf2Sha256(string password, byte[] salt, int iterations, int outputBytes)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password)))
            {
                int hashLen = hmac.HashSize / 8; // 32 bytes for SHA-256
                int blockCount = (int)Math.Ceiling(outputBytes / (double)hashLen);
                byte[] output = new byte[outputBytes];
                int outputOffset = 0;

                for (int blockIndex = 1; blockIndex <= blockCount; blockIndex++)
                {
                    byte[] blockNumber = BitConverter.GetBytes(blockIndex);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(blockNumber);

                    byte[] input = new byte[salt.Length + 4];
                    Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
                    Buffer.BlockCopy(blockNumber, 0, input, salt.Length, 4);

                    byte[] u = hmac.ComputeHash(input);
                    byte[] t = (byte[])u.Clone();

                    for (int i = 1; i < iterations; i++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int k = 0; k < t.Length; k++)
                            t[k] ^= u[k];
                    }

                    int bytesToCopy = Math.Min(hashLen, outputBytes - outputOffset);
                    Buffer.BlockCopy(t, 0, output, outputOffset, bytesToCopy);
                    outputOffset += bytesToCopy;
                }

                return output;
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];

            return diff == 0;
        }
    }
}
