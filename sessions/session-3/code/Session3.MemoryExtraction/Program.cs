using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

// Demo: Basic Memory Extraction from Conversations
// This demo shows how to extract key facts and memories from conversation text
// using Ollama's llama3.2 model via local API.

Console.WriteLine("=== Session 3: Basic Memory Extraction Demo ===\n");

var extractor = new MemoryExtractor();

// Example conversation
var conversation = """
User: What database do you prefer for microservices?
Assistant: For microservices, I'd recommend PostgreSQL for relational data and Redis for caching.
PostgreSQL handles ACID transactions well, and Redis provides sub-millisecond latency.
User: What about monitoring?
Assistant: Use Prometheus for metrics and ELK stack for logs. Prometheus scrapes metrics every 15 seconds by default.
""";

Console.WriteLine("📝 Conversation:\n");
Console.WriteLine(conversation);
Console.WriteLine("\n⏳ Extracting memories...\n");

try
{
    // For demo purposes, this shows the structure
    // In production, this would call Ollama API
    var memories = await extractor.ExtractMemoriesAsync(conversation);
    
    Console.WriteLine("✅ Extracted Memories:\n");
    foreach (var memory in memories)
    {
        Console.WriteLine($"  📌 {memory.Type}: {memory.Content}");
        Console.WriteLine($"     Confidence: {memory.Confidence}%\n");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"ℹ️ Demo mode - API not available: {ex.Message}");
    Console.WriteLine("\n💡 To run with Ollama:");
    Console.WriteLine("  1. Install Ollama: ollama.ai");
    Console.WriteLine("  2. Pull llama3.2: ollama pull llama3.2");
    Console.WriteLine("  3. Run: ollama serve (keep running)");
    Console.WriteLine("  4. Update OLLAMA_URL in code to http://localhost:11434");
}

// Memory extraction service skeleton
class MemoryExtractor
{
    private readonly string _ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";
    private readonly HttpClient _httpClient = new();

    public async Task<List<Memory>> ExtractMemoriesAsync(string conversation)
    {
        var prompt = $"""
        Extract key facts and memories from this conversation. Return as JSON array.
        For each fact, include: type (e.g., "preference", "fact", "config"), content, and confidence (0-100).
        
        Conversation:
        {conversation}
        
        Return only valid JSON array with objects containing: type, content, confidence
        """;

        // In production, this calls Ollama API
        // For now, return demo data
        return new List<Memory>
        {
            new Memory { Type = "preference", Content = "Prefers PostgreSQL for microservices relational data", Confidence = 95 },
            new Memory { Type = "preference", Content = "Redis for caching with sub-millisecond latency", Confidence = 90 },
            new Memory { Type = "config", Content = "Prometheus scrapes metrics every 15 seconds", Confidence = 85 },
            new Memory { Type = "tool", Content = "ELK stack for logging infrastructure", Confidence = 88 }
        };
    }
}

record Memory
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }
}
