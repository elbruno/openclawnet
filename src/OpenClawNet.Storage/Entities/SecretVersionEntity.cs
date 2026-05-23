using System.ComponentModel.DataAnnotations;

namespace OpenClawNet.Storage.Entities;

public sealed class SecretVersionEntity
{
    [Key]
    public Guid Id { get; set; }

    public required string SecretName { get; set; }

    public int Version { get; set; }

    public required string EncryptedValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsCurrent { get; set; }

    public DateTime? SupersededAt { get; set; }

    public SecretEntity? Secret { get; set; }
}
