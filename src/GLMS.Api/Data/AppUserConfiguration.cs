using GLMS.Api.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GLMS.Api.Data;

/// <summary>Users table (Part 3 authentication). Added by the AddAppUsers migration.</summary>
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users", t =>
            t.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Admin', 'LogisticsManager')"));

        b.HasKey(u => u.Id);
        b.Property(u => u.Username).IsRequired().HasMaxLength(50).IsUnicode(false);
        b.HasIndex(u => u.Username).IsUnique();
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256).IsUnicode(false);
        b.Property(u => u.Role).IsRequired().HasMaxLength(30).IsUnicode(false);
        b.Property(u => u.CreatedUtc).HasColumnType("datetime2(0)");
    }
}

public sealed class UserRepository : IUserRepository
{
    private readonly GlmsDbContext _db;
    public UserRepository(GlmsDbContext db) => _db = db;

    public Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalised = (username ?? string.Empty).Trim().ToLowerInvariant();
        return _db.Users.FirstOrDefaultAsync(u => u.Username == normalised, ct);
    }

    public async Task UpdatePasswordHashAsync(AppUser user, string newHash, CancellationToken ct = default)
    {
        user.PasswordHash = newHash;
        await _db.SaveChangesAsync(ct);
    }
}
