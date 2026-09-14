using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Core.Configuration
{
    /// <summary>
    /// Strongly typed view of the <c>JwtSettings</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Replaces stringly-typed <c>IConfiguration</c> lookups inside
    /// <see cref="Services.AuthService"/>. Two things follow from that: the settings
    /// can be validated when the application starts rather than failing on the first
    /// login, and <see cref="ExpiryInHours"/> is finally honoured - it was present in
    /// configuration but ignored by a hardcoded 24-hour expiry.
    /// </remarks>
    public class JwtSettings
    {
        /// <summary>Name of the configuration section these settings bind to.</summary>
        public const string SectionName = "JwtSettings";

        /// <summary>
        /// Symmetric signing key.
        /// </summary>
        /// <remarks>
        /// HMAC-SHA256 requires a key of at least 256 bits, so a shorter key is a
        /// configuration error rather than a preference. Validating the length at
        /// startup prevents the application from running with a key that cannot
        /// securely sign tokens.
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters (256 bits) for HMAC-SHA256.")]
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>Expected token issuer.</summary>
        [Required(AllowEmptyStrings = false)]
        public string Issuer { get; set; } = string.Empty;

        /// <summary>Expected token audience.</summary>
        [Required(AllowEmptyStrings = false)]
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Token lifetime in hours. Defaults to the previously hardcoded value so that
        /// omitting the setting preserves the established behaviour.
        /// </summary>
        [Range(1, 24 * 30)]
        public int ExpiryInHours { get; set; } = 24;
    }
}
