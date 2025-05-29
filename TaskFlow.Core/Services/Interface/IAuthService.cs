using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;

namespace TaskFlow.Core.Services.Interface
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginAsync(LoginDto loginDto);

        Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);

        string GenerateJwtToken(User user);
    }
}
