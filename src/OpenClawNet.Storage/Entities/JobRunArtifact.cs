namespace OpenClawNet.Storage.Entities;

/// <summary>
/// Represents a typed artifact produced by a job run (markdown, JSON, text, file, error).
/// Content ≤64KB is stored inline; larger content spills to disk.
/// </summary>
public class JobRunArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobRunId { get; set; }
    public Guid JobId { get; set; }
    public int Sequence { get; set; } = 0;
    public JobRunArtifactKind ArtifactType { get; set; }
    public string? Title { get; set; }
    public string? ContentInline { get; set; }
    public string? ContentPath { get; set; }
    public long ContentSizeBytes { get; set; }
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }

    public JobRun? Run { get; set; }
}

/// <summary>
/// Artifact type classification.
/// </summary>
public enum JobRunArtifactKind
{
    Text = 0,
    Markdown = 1,
    Json = 2,
    File = 3,
    Link = 4,
    Error = 5
}
