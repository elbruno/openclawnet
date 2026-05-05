using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;
using System.Diagnostics;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Validation tests for Phase 1 success criteria:
/// - Skill lookup latency <2ms (P95)
/// - No regressions in existing tests
/// - Build clean
/// </summary>
public class SkillInjectionValidationTests
{
    [Fact]
    public async Task SkillLookupLatency_P95_LessThan2ms()
    {
        // Test 12: Phase 1 success metric - skill lookup latency
        // Arrange
        var tempDir = SetupTestWorkspace();
        
        try
        {
            var service = CreateService(tempDir);
            var latencies = new List<double>();

            // Act - Run 100 iterations with different keywords
            var testKeywords = new[]
            {
                "blazor mudblazor table migration",
                "hardening security path traversal",
                "ndjson streaming correlation",
                "aspire scaffold dotnet",
                "playwright screenshot testing",
                "agent debugging memory lookup",
                "css flexbox layout constraints",
                "tool-write filesystem safety",
                "threat-model supply-chain",
                "live-testing coverage analysis"
            };

            for (int i = 0; i < 100; i++)
            {
                var keywords = testKeywords[i % testKeywords.Length];
                var sw = Stopwatch.StartNew();
                await service.FindRelevantSkillsAsync(keywords);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            // Assert - P95 latency <2ms
            latencies.Sort();
            var p95Index = (int)(latencies.Count * 0.95);
            var p95Latency = latencies[p95Index];
            var avgLatency = latencies.Average();

            Console.WriteLine($"Skill lookup latency metrics:");
            Console.WriteLine($"  Average: {avgLatency:F3}ms");
            Console.WriteLine($"  P95: {p95Latency:F3}ms");
            Console.WriteLine($"  Min: {latencies.Min():F3}ms");
            Console.WriteLine($"  Max: {latencies.Max():F3}ms");

            p95Latency.Should().BeLessThan(2.0, 
                "P95 skill lookup latency should be <2ms for Phase 1 success");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SkillLookup_CachingPerformance_IsEffective()
    {
        // Validate that caching provides performance benefit
        // Arrange
        var tempDir = SetupTestWorkspace();
        
        try
        {
            var service = CreateService(tempDir);

            // Act - First call (cold cache)
            var sw1 = Stopwatch.StartNew();
            await service.FindRelevantSkillsAsync("blazor mudblazor");
            sw1.Stop();

            // Second call (warm cache)
            var sw2 = Stopwatch.StartNew();
            await service.FindRelevantSkillsAsync("blazor mudblazor");
            sw2.Stop();

            // Assert - Cached call should be faster or similar
            Console.WriteLine($"Cold cache: {sw1.Elapsed.TotalMilliseconds:F3}ms");
            Console.WriteLine($"Warm cache: {sw2.Elapsed.TotalMilliseconds:F3}ms");

            sw2.Elapsed.Should().BeLessThanOrEqualTo(sw1.Elapsed.Add(TimeSpan.FromMilliseconds(1)),
                "cached lookup should not be slower than cold lookup");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SkillService_LoadsFromRealInventory_WithoutErrors()
    {
        // Test 13: No regressions - verify skill service can load from real inventory
        // Arrange
        var workspacePath = FindWorkspaceRoot();
        
        if (workspacePath == null || !File.Exists(Path.Combine(workspacePath, ".squad", "SKILLS_INVENTORY.md")))
        {
            Console.WriteLine("Skipping: SKILLS_INVENTORY.md not found");
            return;
        }

        // Act - Load real inventory
        var service = new DefaultSkillService(
            NullLogger<DefaultSkillService>.Instance,
            Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath }));

        // Assert - Should not throw
        var act = async () => await service.FindRelevantSkillsAsync("test");
        act.Should().NotThrowAsync("loading real inventory should not throw");
    }

    private static DefaultSkillService CreateService(string workspacePath)
    {
        var options = Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath });
        return new DefaultSkillService(NullLogger<DefaultSkillService>.Instance, options);
    }

    private static string SetupTestWorkspace()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var squadDir = Path.Combine(tempDir, ".squad");
        Directory.CreateDirectory(squadDir);
        
        var inventoryContent = @"# Skills Inventory

**Last Updated:** 2026-04-27  

## Quick Reference

| Skill Name | Extracted | Extracted By | Confidence | Keywords |
|------------|-----------|--------------|------------|----------|
| blazor-table-mudblazor-migration | 2026-04-22 | helly | **HIGH** | blazor, mudblazor, datagrid, bootstrap, table-migration, frontend, v9, dotnet-10 |
| tool-write-hardening-review | 2026-05-21 | drummond | **HIGH** | hardening, security, path-traversal, containment, tool-write, llm-safety, filesystem |
| aspire-blazor-scaffold | 2026-04-23 | mark | **HIGH** | aspire, blazor-server, scaffold, mudblazor, service-discovery, dotnet-10 |
| ndjson-tail | 2026-04-27 | petey | **HIGH** | ndjson, streaming, blazor, db-tail, polling, live-updates, http-streaming |
| ndjson-request-correlation | 2026-04-27 | petey | **HIGH** | ndjson, correlation, async, tool-approval, mid-stream, guid, taskcompletionsource |
| skills-spec-audit | 2026-04-26 | petey | **HIGH** | skills, spec-alignment, agentskills-io, maf, progressive-disclosure, audit |
| mudblazor-blazor-server-setup | 2026-04-27 | helly | **HIGH** | mudblazor, blazor-server, bootstrap, theming, setup, typography, dotnet-10 |
| external-bundle-threat-model | 2026-05-22 | drummond | **HIGH** | hardening, threat-model, supply-chain, prompt-injection, external-content, security |
| live-test-coverage | 2026-04-30 | petey | **HIGH** | testing, llm-testing, live-tests, provider-testing, ollama, azure-openai, coverage-analysis |
| blazor-screenshot-capture | 2026-04-27 | petey | **MEDIUM** | playwright, aspire, screenshot, blazor-server, documentation, chromium |
| blazor-flex-height-constraint | 2026-04-27 | helly | **MEDIUM** | blazor, css, flexbox, layout, height-constraint, overflow |
";
        
        var inventoryPath = Path.Combine(squadDir, "SKILLS_INVENTORY.md");
        File.WriteAllText(inventoryPath, inventoryContent);
        
        return tempDir;
    }

    private static string? FindWorkspaceRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".squad")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }
}
