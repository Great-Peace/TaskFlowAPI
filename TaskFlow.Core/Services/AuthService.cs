using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Core.Configuration;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;
using TaskFlow.Core.Services.Interface;

namespace TaskFlow.Core.Services
{
    /// <summary>
    /// Registration, authentication and JWT issuance.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly JwtSettings _jwtSettings;
        private readonly IPasswordHasher _passwordHasher;
        private readonly TimeProvider _timeProvider;

        /// <summary>Creates the service.</summary>
        /// <param name="unitOfWork">Persistence gateway.</param>
        /// <param name="jwtSettings">Validated JWT configuration.</param>
        /// <param name="passwordHasher">Password hashing strategy.</param>
        /// <param name="timeProvider">
        /// Clock used for token expiry. Injected rather than read from
        /// <c>DateTime.UtcNow</c> so that expiry is assertable in tests.
        /// </param>
        public AuthService(
            IUnitOfWork unitOfWork,
            IOptions<JwtSettings> jwtSettings,
            IPasswordHasher passwordHasher,
            TimeProvider timeProvider)
        {
            _unitOfWork = unitOfWork;
            _jwtSettings = jwtSettings.Value;
            _passwordHasher = passwordHasher;
            _timeProvider = timeProvider;
        }

        /// <summary>Issues a signed JWT for <paramref name="user"/>.</summary>
        public string GenerateJwtToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var issuedAt = _timeProvider.GetUtcNow().UtcDateTime;

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: issuedAt.AddHours(_jwtSettings.ExpiryInHours),
                signingCredentials: credentials
             );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>Authenticates a user and issues a token.</summary>
        /// <exception cref="UnauthorizedAccessException">
        /// The e-mail is unknown or the password does not match. The same message is
        /// used for both so the response cannot be used to enumerate accounts.
        /// </exception>
        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(loginDto.Email);

            if (user == null || !_passwordHasher.Verify(loginDto.Password, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Invalid email or password");
            }

            var token = GenerateJwtToken(user);

            return new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role
            };
        }

        /// <summary>Creates a new account and issues a token for it.</summary>
        /// <exception cref="InvalidOperationException">The e-mail is already registered.</exception>
        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {
            if (await _unitOfWork.Users.EmailExistsAsync(registerDto.Email))
            {
                throw new InvalidOperationException("Email already exists");
            }

            var user = new User
            {
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                Email = registerDto.Email,
                PasswordHash = _passwordHasher.Hash(registerDto.Password),
                Role = "User",
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            };

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            var token = GenerateJwtToken(user);

            return new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role
            };
        }
    }
}
