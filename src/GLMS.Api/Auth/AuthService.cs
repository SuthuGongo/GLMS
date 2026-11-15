using System.Security.Claims;
using GLMS.Shared;
using GLMS.Shared.Dtos;
using Microsoft.AspNetCore.Identity;

namespace GLMS.Api.Auth;

/// <summary>Settings for issuing and validating tokens (section "Jwt" in configuration).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "glms-api";
    public string Audience { get; set; } = "glms-clients";

    /// <summary>HMAC-SHA256 key, at least 32 characters. Supplied via environment variable Jwt__SigningKey in Docker.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 120;
}

/// <summary>Creates a signed JWT for a user. Implemented by <see cref="JwtTokenService"/>.</summary>
public interface ITokenService
{
    (string Token, DateTime ExpiresUtc) CreateToken(AppUser user);
}

/// <summary>
/// Login use case: checks the username and password against the Users table and, if they match, issues a JWT.
/// Kept out of the controller so the controller only translates HTTP.
/// </summary>
public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly ITokenService _tokens;

    public AuthService(IUserRepository users, IPasswordHasher<AppUser> hasher, ITokenService tokens)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
    }

    /// <summary>Returns null when the username or password is wrong (the caller answers 401 without saying which).</summary>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _users.FindByUsernameAsync(request.Username, ct);
        if (user is null) return null;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await _users.UpdatePasswordHashAsync(user, _hasher.HashPassword(user, request.Password), ct);

        var (token, expires) = _tokens.CreateToken(user);
        return new LoginResponse(token, expires, user.Username, user.DisplayName, user.Role);
    }

    public static CurrentUserDto Describe(ClaimsPrincipal principal) => new(
        principal.FindFirstValue(GlmsClaimTypes.Name) ?? string.Empty,
        principal.FindFirstValue(GlmsClaimTypes.DisplayName) ?? string.Empty,
        principal.FindFirstValue(GlmsClaimTypes.Role) ?? string.Empty);
}

/// <summary>
/// Short JWT claim names. Inbound claim mapping is switched off (AuthenticationSetup) and these are set as the
/// name and role claim types, so [Authorize(Roles = ...)] and User.Identity.Name read them directly.
/// </summary>
public static class GlmsClaimTypes
{
    public const string Name = "name";
    public const string Role = "role";
    public const string DisplayName = "display_name";
}
