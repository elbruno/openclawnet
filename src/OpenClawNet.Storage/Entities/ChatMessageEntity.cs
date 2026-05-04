namespace OpenClawNet.Storage.Entities;

public sealed class ChatMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolCallsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int OrderIndex { get; set; }
    
    // Tool approval bubbles - Phase A
    public string MessageType { get; set; } = "Chat";
    public string? ToolName { get; set; }
    public string? ToolArgsJson { get; set; }
    public string? ToolDecision { get; set; }
    public string? ToolDecidedBy { get; set; }
    public DateTime? ToolDecidedAt { get; set; }
    
    public ChatSession Session { get; set; } = null!;
}
