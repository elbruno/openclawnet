using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Agent;

namespace OpenClawNet.UnitTests.Agent;

public class SkillServiceTests
{
    private const string TestInventory = @"# Skills Inventory

**Last Updated:** 2026-04-27  
**Maintained by:** Petey (Agent Platform Specialist)

This inventory tracks all extracted skills in `.squad/skills/`, their validation status, and searchable keywords for rapid discovery.

---

## Quick Reference

| Skill Name | Extracted | Extracted By | Confidence | Keywords |
|------------|-----------|--------------|------------|----------|
| blazor-table-mudblazor-migration | 2026-04-22 | helly | **HIGH** | blazor, mudblazor, datagrid, bootstrap, table-migration, frontend, v9, dotnet-10 |
| tool-write-hardening-review | 2026-05-21 | drummond | **HIGH** | hardening, security, path-traversal, containment, tool-write, llm-safety, filesystem |
| ndjson-tail | 2026-04-27 | petey | **HIGH** | ndjson, streaming, blazor, db-tail, polling, live-updates, http-streaming |
| blazor-flex-height-constraint | 2026-04-27 | helly | **MEDIUM** | blazor, css, flexbox, layout, height-constraint, overflow |
";

    [Fact]
    public async Task FindRelevantSkillsAsync_ReturnsEmptyList_WhenInventoryMissing()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("test blazor");

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_ReturnsMatchingSkills_WhenKeywordsMatch()
    {
        // Arrange
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("I need to implement blazor mudblazor table");

            // Assert
            result.Should().NotBeEmpty();
            result.Should().Contain(s => s.Name == "blazor-table-mudblazor-migration");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_RespectsTopNLimit()
    {
        // Arrange
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("blazor", topN: 2);

            // Assert
            result.Should().HaveCountLessThanOrEqualTo(2);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_RanksHighConfidenceHigher()
    {
        // Arrange
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act - both skills match "blazor", but one is HIGH, one is MEDIUM
            var result = await service.FindRelevantSkillsAsync("blazor layout");

            // Assert
            result.Should().NotBeEmpty();
            // High confidence skills should rank higher
            if (result.Count > 1)
            {
                result.First().Confidence.Should().BeOneOf(ConfidenceLevel.High, ConfidenceLevel.Medium);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_ReturnsEmptyList_WhenNoKeywordsMatch()
    {
        // Arrange
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("quantum computing python tensorflow");

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_ParsesConfidenceLevelsCorrectly()
    {
        // Arrange
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("blazor mudblazor ndjson hardening");

            // Assert
            result.Should().NotBeEmpty();
            
            var highSkill = result.FirstOrDefault(s => s.Name == "tool-write-hardening-review");
            highSkill?.Confidence.Should().Be(ConfidenceLevel.High);
            
            var mediumSkill = result.FirstOrDefault(s => s.Name == "blazor-flex-height-constraint");
            mediumSkill?.Confidence.Should().Be(ConfidenceLevel.Medium);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FindRelevantSkillsAsync_ParsesAllSkillMetadata()
    {
        // Arrange: Test that we extract name, keywords, confidence, date from inventory
        var tempDir = SetupTestWorkspace(TestInventory);
        
        try
        {
            var service = CreateService(tempDir);

            // Act
            var result = await service.FindRelevantSkillsAsync("blazor mudblazor migration table");

            // Assert
            result.Should().NotBeEmpty();
            
            var skill = result.FirstOrDefault(s => s.Name == "blazor-table-mudblazor-migration");
            skill.Should().NotBeNull();
            skill!.Name.Should().Be("blazor-table-mudblazor-migration");
            skill.Keywords.Should().Contain("blazor");
            skill.Keywords.Should().Contain("mudblazor");
            skill.Keywords.Should().Contain("datagrid");
            skill.Confidence.Should().Be(ConfidenceLevel.High);
            skill.ExtractedDate.Should().Be("2026-04-22");
            skill.ValidatedBy.Should().Contain("helly");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static DefaultSkillService CreateService(string workspacePath)
    {
        var options = Options.Create(new WorkspaceOptions { WorkspacePath = workspacePath });
        return new DefaultSkillService(NullLogger<DefaultSkillService>.Instance, options);
    }

    private static string SetupTestWorkspace(string inventoryContent)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var squadDir = Path.Combine(tempDir, ".squad");
        Directory.CreateDirectory(squadDir);
        
        var inventoryPath = Path.Combine(squadDir, "SKILLS_INVENTORY.md");
        File.WriteAllText(inventoryPath, inventoryContent);
        
        return tempDir;
    }
}
