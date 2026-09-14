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
        public bool Verify(string password, string hashedPassword)
        {
            var combined = Convert.FromBase64String(hashedPassword);

            var salt = new byte[SaltSizeInBytes];
            var hash = new byte[HashSizeInBytes];
            Array.Copy(combined, 0, salt, 0, SaltSizeInBytes);
            Array.Copy(combined, SaltSizeInBytes, hash, 0, HashSizeInBytes);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, Algorithm);
            var candidate = pbkdf2.GetBytes(HashSizeInBytes);

            // Constant-time comparison: avoids leaking how much of the hash matched.
            return CryptographicOperations.FixedTimeEquals(hash, candidate);
        }
    }
}
