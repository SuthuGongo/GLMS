using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GLMS.Api.Auth;

/// <summary>Issues HMAC-SHA256 signed JWTs. The same key, issuer and audience are used to validate them (AuthenticationSetup).</summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly GLMS.Core.Abstractions.IClock _clock;

    public JwtTokenService(IOptions<JwtOptions> options, GLMS.Core.Abstractions.IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public (string Token, DateTime ExpiresUtc) CreateToken(AppUser user)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(GlmsClaimTypes.Name, user.Username),
            new Claim(GlmsClaimTypes.Role, user.Role),
            new Claim(GlmsClaimTypes.DisplayName, user.DisplayName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
