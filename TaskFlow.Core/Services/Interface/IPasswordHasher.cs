namespace TaskFlow.Core.Services.Interface
{
    /// <summary>
    /// Hashes and verifies user passwords.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="Services.AuthService"/> so that the hashing strategy is
    /// independently testable and can be reused by test-data seeding without
    /// duplicating the algorithm.
    /// </remarks>
    public interface IPasswordHasher
    {
        /// <summary>Produces a self-describing hash (salt embedded) for <paramref name="password"/>.</summary>
        string Hash(string password);

        /// <summary>
        /// Verifies <paramref name="password"/> against a hash previously produced by <see cref="Hash"/>.
        /// </summary>
        /// <returns><c>true</c> when the password matches.</returns>
        bool Verify(string password, string hashedPassword);
    }
}
