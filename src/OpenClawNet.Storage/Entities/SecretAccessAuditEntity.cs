using System.ComponentModel.DataAnnotations;

namespace OpenClawNet.Storage.Entities;

public sealed class SecretAccessAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public long? Sequence { get; set; }

    public required string SecretName { get; set; }

    public required string CallerType { get; set; }

    public required string CallerId { get; set; }

    public string? SessionId { get; set; }

    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;

    public bool Success { get; set; }

    public string? PreviousRowHash { get; set; }

    public string? RowHash { get; set; }
}
