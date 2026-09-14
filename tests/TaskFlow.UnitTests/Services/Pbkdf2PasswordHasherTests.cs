using TaskFlow.Core.Services;

namespace TaskFlow.UnitTests.Services;

/// <summary>
/// Behaviour of the password hashing strategy.
/// </summary>
/// <remarks>
/// These assert observable behaviour - that a password round-trips, that a wrong
/// password is rejected, that two hashes of the same password differ - rather than
/// the specific algorithm. The algorithm is an implementation choice that should be
/// changeable without rewriting the tests.
/// </remarks>
public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_accepts_the_password_that_was_hashed()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_rejects_a_different_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("Correct horse battery staple", hash));
    }

    [Fact]
    public void Hash_never_stores_the_password_in_clear_text()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.DoesNotContain("correct horse battery staple", hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        // A per-password random salt is what stops identical passwords from being
        // identifiable in a stolen database, and defeats precomputed rainbow tables.
        var first = _hasher.Hash("same password");
        var second = _hasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("same password", first));
        Assert.True(_hasher.Verify("same password", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("pässwörd with ünicode")]
    [InlineData("a-very-long-password-that-goes-well-beyond-any-reasonable-input-length-0123456789")]
    public void Passwords_of_any_shape_round_trip(string password)
    {
        var hash = _hasher.Hash(password);

        Assert.True(_hasher.Verify(password, hash));
    }

    /// <summary>
    /// A stored hash that is not valid Base64, or is too short to contain a salt and
    /// a key, must be treated as "does not match" rather than crashing.
    /// </summary>
    /// <remarks>
    /// This is a real defect found during the Phase 0 assessment. Before the fix,
    /// <c>Verify</c> propagated <c>FormatException</c> or <c>ArgumentException</c>,
    /// which the global exception middleware turned into a 500. A corrupt or
    /// truncated hash in the database therefore produced "internal server error"
    /// instead of "invalid email or password", leaking that the account exists and
    /// that its stored data is malformed.
    /// </remarks>
    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("")]
    [InlineData("dG9vLXNob3J0")]          // valid Base64, but far fewer than 64 bytes
    [InlineData("AAAAAAAAAAAAAAAAAAAA")]  // valid Base64, still too short
    public void Verify_returns_false_for_a_malformed_stored_hash(string malformedHash)
    {
        var result = _hasher.Verify("any password", malformedHash);

        Assert.False(result);
    }
}
