using System.Text;
using GLMS.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GLMS.Api.Infrastructure;

/// <summary>JWT bearer authentication: every /api endpoint except POST /api/auth/login needs a valid token.</summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddGlmsJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be set to at least 32 characters (appsettings.Development.json, or the Jwt__SigningKey environment variable in Docker).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;           // keep the short "name"/"role" claims as issued
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = GlmsClaimTypes.Name,
                    RoleClaimType = GlmsClaimTypes.Role
                };
            });

        services.AddAuthorization();
        services.AddScoped<ITokenService, JwtTokenService>();
        return services;
    }
}
