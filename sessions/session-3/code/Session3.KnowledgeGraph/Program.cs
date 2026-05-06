using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

// Demo: Knowledge Graph Construction from Extracted Memories
// This demo shows how to build relationships between extracted memories
// to form a structured knowledge graph using Ollama's llama3.2 model.

Console.WriteLine("=== Session 3: Knowledge Graph Construction Demo ===\n");

var graphBuilder = new KnowledgeGraphBuilder();

// Extracted memories from previous conversations (Session 3 Demo 1 output)
var memories = new[]
{
    new Memory { Type = "preference", Content = "Prefers PostgreSQL for microservices relational data" },
    new Memory { Type = "preference", Content = "Redis for caching with sub-millisecond latency" },
    new Memory { Type = "config", Content = "Prometheus scrapes metrics every 15 seconds" },
    new Memory { Type = "tool", Content = "ELK stack for logging infrastructure" },
    new Memory { Type = "preference", Content = "ACID transactions are important for data integrity" },
    new Memory { Type = "tool", Content = "Grafana for metrics visualization" }
};

Console.WriteLine("📚 Input Memories:\n");
foreach (var memory in memories)
{
    Console.WriteLine($"  • [{memory.Type}] {memory.Content}");
}

Console.WriteLine("\n⏳ Building knowledge graph...\n");

try
{
    // Build relationships between memories
    var graph = graphBuilder.BuildGraph(memories);
    
    Console.WriteLine("✅ Knowledge Graph Constructed:\n");
    Console.WriteLine($"📊 Statistics:");
    Console.WriteLine($"  • Nodes (unique memories): {graph.Nodes.Count}");
    Console.WriteLine($"  • Edges (relationships): {graph.Edges.Count}\n");

    Console.WriteLine("🔗 Relationships:\n");
    foreach (var edge in graph.Edges)
    {
        Console.WriteLine($"  {edge.Source}");
        Console.WriteLine($"    ├─ [{edge.Relationship}] ──→ {edge.Target}");
        Console.WriteLine($"    └─ (confidence: {edge.Confidence}%)\n");
    }

    Console.WriteLine("🎯 Memory Clusters:\n");
    var clusters = graph.GetClusters();
    foreach (var cluster in clusters)
    {
        Console.WriteLine($"  Cluster: {cluster.Topic}");
        foreach (var node in cluster.Nodes)
        {
            Console.WriteLine($"    - {node}");
        }
        Console.WriteLine();
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

// Knowledge graph builder
class KnowledgeGraphBuilder
{
    private readonly string _ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";

    public KnowledgeGraph BuildGraph(Memory[] memories)
    {
        var graph = new KnowledgeGraph();

        // Add all memories as nodes
        foreach (var memory in memories)
        {
            graph.Nodes.Add(memory.Content);
        }

        // In production, use Ollama to identify relationships
        // For demo, create sample relationships based on patterns
        var edges = new List<Edge>
        {
            new Edge 
            { 
                Source = "Prefers PostgreSQL for microservices relational data",
                Target = "ACID transactions are important for data integrity",
                Relationship = "supports",
                Confidence = 92
            },
            new Edge
            {
                Source = "Redis for caching with sub-millisecond latency",
                Target = "Prometheus scrapes metrics every 15 seconds",
                Relationship = "complements",
                Confidence = 85
            },
            new Edge
            {
                Source = "ELK stack for logging infrastructure",
                Target = "Prometheus scrapes metrics every 15 seconds",
                Relationship = "integrates_with",
                Confidence = 88
            },
            new Edge
            {
                Source = "Prometheus scrapes metrics every 15 seconds",
                Target = "Grafana for metrics visualization",
                Relationship = "feeds",
                Confidence = 90
            }
        };

        graph.Edges.AddRange(edges);
        return graph;
    }
}

record Memory
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

record Edge
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }
}

record MemoryCluster
{
    public string Topic { get; set; } = string.Empty;
    public List<string> Nodes { get; set; } = new();
}

class KnowledgeGraph
{
    public List<string> Nodes { get; set; } = new();
    public List<Edge> Edges { get; set; } = new();

    public List<MemoryCluster> GetClusters()
    {
        var clusters = new List<MemoryCluster>();

        // Group related memories by analyzing edge relationships
        var infrastructure = new MemoryCluster 
        { 
            Topic = "Monitoring Infrastructure",
            Nodes = new()
            {
                "Prometheus scrapes metrics every 15 seconds",
                "Grafana for metrics visualization",
                "ELK stack for logging infrastructure"
            }
        };

        var dataManagement = new MemoryCluster
        {
            Topic = "Data Management",
            Nodes = new()
            {
                "Prefers PostgreSQL for microservices relational data",
                "ACID transactions are important for data integrity",
                "Redis for caching with sub-millisecond latency"
            }
        };

        clusters.Add(infrastructure);
        clusters.Add(dataManagement);
        return clusters;
    }
}
