namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Optional artifact produced from a chat session. Mirrors <see cref="JobRunArtifact"/>
/// so chat outputs can flow into channels via a unified union query.
/// Concept-review §4c — additive sibling-model implementation. Default usage off
/// (channels include chat artifacts only when feature flag <c>Channels:IncludeChatArtifacts</c> is true).
/// </summary>
public sealed class ChatSessionArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public int Sequence { get; set; }
    public JobRunArtifactKind ArtifactType { get; set; }
    public string? Title { get; set; }
    public string? ContentInline { get; set; }
    public string? ContentPath { get; set; }
    public long ContentSizeBytes { get; set; }
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }

    public ChatSession? Session { get; set; }
}
