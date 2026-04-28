namespace OpenClawNet.Storage.Entities;

public sealed class SkillVector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SkillName { get; set; }
    public required byte[] Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
