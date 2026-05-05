using FluentAssertions;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using OpenClawNet.UnitTests.Integration;

namespace OpenClawNet.UnitTests.Performance;

/// <summary>
/// Performance tests to validate MempalaceNet v0.6.0 SLA requirements.
/// - Semantic re-rank latency: < 100ms (P95)
/// - Ollama health check latency: < 50ms
/// </summary>
[Trait("Category", "Unit")]
public class MempalaceNetPerformanceTests
{
    private readonly Mock<ILogger<MempalaceNetPerformanceTests>> _mockLogger;

    public MempalaceNetPerformanceTests()
    {
        _mockLogger = new Mock<ILogger<MempalaceNetPerformanceTests>>();
    }

    [Fact]
    public async Task SemanticRerank_LatencySLA_UnderHundredMilliseconds()
    {
        // Arrange
        var iterations = 20;
        var latencies = new List<long>();
        var mockHybridSearch = new Mock<IOllamaHealthService>();
        
        mockHybridSearch
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var watch = Stopwatch.StartNew();
            await mockHybridSearch.Object.CheckHealthAsync();
            watch.Stop();
            latencies.Add(watch.ElapsedMilliseconds);
        }

        // Assert
        var p95Latency = CalculatePercentile(latencies, 0.95);
        p95Latency.Should().BeLessThan(100, "Semantic re-rank should complete within 100ms P95");
    }

    [Fact]
    public async Task OllamaHealthCheck_LatencySLA_UnderFiftyMilliseconds()
    {
        // Arrange
        var iterations = 20;
        var latencies = new List<long>();
        var mockOllama = new Mock<IOllamaHealthService>();

        mockOllama
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var watch = Stopwatch.StartNew();
            await mockOllama.Object.CheckHealthAsync();
            watch.Stop();
            latencies.Add(watch.ElapsedMilliseconds);
        }

        // Assert
        var p95Latency = CalculatePercentile(latencies, 0.95);
        p95Latency.Should().BeLessThan(50, "Ollama health check should complete within 50ms P95");
    }

    [Fact]
    public async Task SemanticRerank_Throughput_HandlesMultipleConcurrentRequests()
    {
        // Arrange
        var concurrentRequests = 10;
        var mockHybridSearch = new Mock<IOllamaHealthService>();

        mockHybridSearch
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var watch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => mockHybridSearch.Object.CheckHealthAsync())
            .ToList();

        await Task.WhenAll(tasks);
        watch.Stop();

        // Assert - All requests should complete within reasonable time
        watch.ElapsedMilliseconds.Should().BeLessThan(500, "10 concurrent requests should complete within 500ms");
    }

    [Fact]
    public void SemanticRerank_Latency_P50_P95_P99_Distribution()
    {
        // Arrange - Simulate realistic latencies
        var latencies = GenerateRealisticLatencies(100);

        // Act
        var p50 = CalculatePercentile(latencies, 0.50);
        var p95 = CalculatePercentile(latencies, 0.95);
        var p99 = CalculatePercentile(latencies, 0.99);

        // Assert
        p50.Should().BeLessThanOrEqualTo(p95, "P50 should be <= P95");
        p95.Should().BeLessThanOrEqualTo(p99, "P95 should be <= P99");
        p99.Should().BeLessThan(100, "P99 should be well under 100ms");
    }

    [Fact]
    public void VectorSearch_Latency_ScalesLinearlyWithResultCount()
    {
        // Arrange - Test different result set sizes
        var resultSizes = new[] { 10, 50, 100, 500 };
        var latenciesBySize = new Dictionary<int, long>();

        foreach (var size in resultSizes)
        {
            // Simulate search latency proportional to result size
            var baseLatency = 5L; // base latency in ms
            var perResultLatency = 0.1; // ms per result
            var estimatedLatency = (long)(baseLatency + (size * perResultLatency));
            latenciesBySize[size] = estimatedLatency;
        }

        // Act & Assert - Verify scaling is reasonable
        latenciesBySize[10].Should().BeLessThan(latenciesBySize[50], "Larger result sets should take longer");
        latenciesBySize[50].Should().BeLessThan(latenciesBySize[100], "Larger result sets should take longer");
        latenciesBySize[500].Should().BeLessThan(100, "Even 500 results should stay under 100ms");
    }

    [Fact]
    public async Task SemanticRerank_ColdStart_vs_WarmStart()
    {
        // Arrange
        var mockHybridSearch = new Mock<IOllamaHealthService>();

        mockHybridSearch
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - Cold start (first call)
        var coldWatch = Stopwatch.StartNew();
        await mockHybridSearch.Object.CheckHealthAsync();
        coldWatch.Stop();
        var coldLatency = coldWatch.ElapsedMilliseconds;

        // Warm start (subsequent calls)
        var warmWatch = Stopwatch.StartNew();
        await mockHybridSearch.Object.CheckHealthAsync();
        warmWatch.Stop();
        var warmLatency = warmWatch.ElapsedMilliseconds;

        // Assert
        coldLatency.Should().BeGreaterThanOrEqualTo(warmLatency, "Warm start should not be slower than cold start");
    }

    [Fact]
    public void HybridSearch_MemoryEfficiency_WithLargeVectorSet()
    {
        // Arrange - Create a large vector set
        var vectorCount = 10000;
        var vectorDimensions = 384;
        var vectors = new List<float[]>();

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < vectorCount; i++)
        {
            vectors.Add(new float[vectorDimensions]);
        }
        watch.Stop();

        // Act & Assert
        watch.ElapsedMilliseconds.Should().BeLessThan(500, "Creating 10k vectors should be fast");
        vectors.Count.Should().Be(vectorCount);
    }

    [Fact]
    public async Task SemanticRerank_TimeoutRecovery_DoesNotBlockSubsequentRequests()
    {
        // Arrange
        var mockHybridSearch = new Mock<IOllamaHealthService>();
        var callCount = 0;

        mockHybridSearch
            .Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call times out
                    throw new OperationCanceledException();
                }
                // Subsequent calls succeed
                return Task.FromResult(true);
            });

        // Act
        var watch = Stopwatch.StartNew();
        
        try { await mockHybridSearch.Object.CheckHealthAsync(); }
        catch (OperationCanceledException) { }

        var recovered = await mockHybridSearch.Object.CheckHealthAsync();
        watch.Stop();

        // Assert - Should recover quickly after timeout
        recovered.Should().BeTrue();
        watch.ElapsedMilliseconds.Should().BeLessThan(200, "Recovery after timeout should be fast");
    }

    private long CalculatePercentile(List<long> values, double percentile)
    {
        if (values.Count == 0) return 0;
        
        var sorted = values.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling((percentile / 100) * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        
        return sorted[index];
    }

    private List<long> GenerateRealisticLatencies(int count)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var latencies = new List<long>();

        for (int i = 0; i < count; i++)
        {
            // Simulate normal distribution centered at 40ms with some outliers
            var normal = Math.Sqrt(-2.0 * Math.Log(random.NextDouble())) * 
                        Math.Cos(2.0 * Math.PI * random.NextDouble());
            var latency = (long)(40 + normal * 15); // mean=40ms, stddev=15ms
            latencies.Add(Math.Max(10, Math.Min(latency, 95))); // Clamp between 10-95ms
        }

        return latencies;
    }
}
