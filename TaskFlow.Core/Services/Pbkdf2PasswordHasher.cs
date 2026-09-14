using System.Security.Cryptography;
using TaskFlow.Core.Services.Interface;

namespace TaskFlow.Core.Services
{
    /// <summary>
    /// PBKDF2-SHA256 password hasher.
    /// </summary>
    /// <remarks>
    /// Storage format is a single Base64 string of 64 bytes: the 32-byte salt
    /// followed by the 32-byte derived key. This is the format that was already
    /// in use before the hashing logic was extracted from <see cref="AuthService"/>,
    /// so existing stored hashes remain valid.
    /// </remarks>
    public class Pbkdf2PasswordHasher : IPasswordHasher
    {
        private const int SaltSizeInBytes = 32;
        private const int HashSizeInBytes = 32;
        private const int Iterations = 10_000;

        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        /// <inheritdoc />
        public string Hash(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSizeInBytes];
            rng.GetBytes(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, Algorithm);
            var hash = pbkdf2.GetBytes(HashSizeInBytes);

            var combined = new byte[SaltSizeInBytes + HashSizeInBytes];
            Array.Copy(salt, 0, combined, 0, SaltSizeInBytes);
            Array.Copy(hash, 0, combined, SaltSizeInBytes, HashSizeInBytes);

            return Convert.ToBase64String(combined);
        }

        /// <inheritdoc />
        /// <remarks>
        /// A stored hash that cannot be decoded is treated as a non-match rather than
        /// an error. Throwing here would surface a corrupt or truncated database value
        /// as an internal server error on login, which both breaks the caller's
        /// contract and reveals that the account exists.
        /// </remarks>
        public bool Verify(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            Span<byte> combined = stackalloc byte[SaltSizeInBytes + HashSizeInBytes];
            if (!Convert.TryFromBase64String(hashedPassword, combined, out var decodedLength)
                || decodedLength != SaltSizeInBytes + HashSizeInBytes)
            {
                return false;
            }

            var salt = new byte[SaltSizeInBytes];
            var hash = new byte[HashSizeInBytes];
            combined[..SaltSizeInBytes].CopyTo(salt);
            combined[SaltSizeInBytes..].CopyTo(hash);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, Algorithm);
            var candidate = pbkdf2.GetBytes(HashSizeInBytes);

            // Constant-time comparison: avoids leaking how much of the hash matched.
            return CryptographicOperations.FixedTimeEquals(hash, candidate);
        }
    }
}
