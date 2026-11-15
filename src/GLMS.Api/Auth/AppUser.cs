namespace GLMS.Api.Auth;

/// <summary>
/// A person who may sign in to GLMS (table Users). Only a salted PBKDF2 hash of the password is stored
/// (ASP.NET Core's PasswordHasher), never the password itself.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="GLMS.Shared.GlmsRoles"/>; becomes the role claim in the JWT.</summary>
    public string Role { get; set; } = GLMS.Shared.GlmsRoles.Manager;

    public DateTime CreatedUtc { get; set; }
}

public interface IUserRepository
{
    Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task UpdatePasswordHashAsync(AppUser user, string newHash, CancellationToken ct = default);
}
