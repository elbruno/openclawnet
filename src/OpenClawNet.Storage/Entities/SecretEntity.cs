using System.ComponentModel.DataAnnotations;

namespace OpenClawNet.Storage.Entities;

/// <summary>
/// A named secret value (API key, token, endpoint, etc.) used by tools and
/// integrations. The <see cref="EncryptedValue"/> column is encrypted at rest
/// via ASP.NET Core DataProtection so the SQLite file alone is not enough to
/// disclose the plaintext.
/// </summary>
public class SecretEntity
{
    [Key]
    public required string Name { get; set; }

    /// <summary>Protected (DataProtection-encrypted) ciphertext of the value.</summary>
    public required string EncryptedValue { get; set; }

    /// <summary>Optional human-readable description (e.g. "GitHub PAT for the github tool").</summary>
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public DateTime? PurgeAfter { get; set; }

    public ICollection<SecretVersionEntity> Versions { get; set; } = [];
}
