namespace KasaManager.Domain.Identity;

/// <summary>
/// Uygulama kullanıcısı.
/// Cookie Authentication ile kullanılır.
/// </summary>
public sealed class KasaUser
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>
    /// "Admin" veya "User"
    /// </summary>
    public string Role { get; set; } = "User";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
