using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Agent;
using OpenClawNet.Gateway.Services;

namespace OpenClawNet.UnitTests.Agent;

/// <summary>
/// Validates DI registration for semantic skill ranking services (Phase 2B).
/// Ensures ISemanticSkillRanker and IHybridSearchService are correctly wired.
/// </summary>
public sealed class SemanticSkillRankerRegistrationTests
{
    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        // Register the services as they would be in Program.cs
        services.AddScoped<IHybridSearchService, DefaultHybridSearchService>();
        services.AddScoped<ISemanticSkillRanker, SemanticSkillRanker>();
        
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ISemanticSkillRanker_CanBeResolved()
    {
        // Arrange
        using var serviceProvider = BuildServiceProvider();

        // Act
        var ranker = serviceProvider.GetRequiredService<ISemanticSkillRanker>();

        // Assert
        ranker.Should().NotBeNull();
        ranker.Should().BeOfType<SemanticSkillRanker>();
    }

    [Fact]
    public void ISemanticSkillRanker_IsRegisteredAsScoped()
    {
        // Arrange
        using var serviceProvider = BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<ISemanticSkillRanker>();
        var instance2 = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<ISemanticSkillRanker>();

        // Assert
        instance1.Should().NotBeSameAs(instance2, "Scoped services should create new instances per scope");
    }

    [Fact]
    public void IHybridSearchService_CanBeResolved()
    {
        // Arrange
        using var serviceProvider = BuildServiceProvider();

        // Act
        var service = serviceProvider.GetRequiredService<IHybridSearchService>();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeOfType<DefaultHybridSearchService>();
    }

    [Fact]
    public void SemanticSkillRanker_ResolvesWithHybridSearchServiceDependency()
    {
        // Arrange
        using var serviceProvider = BuildServiceProvider();

        // Act
        var ranker = serviceProvider.GetRequiredService<ISemanticSkillRanker>();

        // Assert
        ranker.Should().NotBeNull();
        // Verify that SemanticSkillRanker was successfully instantiated (which requires IHybridSearchService)
        ranker.Should().BeAssignableTo<ISemanticSkillRanker>();
    }

    [Fact]
    public void SkillSummary_ContainsSemanticFields()
    {
        // Arrange
        var skillSummary = new SkillSummary
        {
            Name = "test-skill",
            Description = "Test skill description",
            Keywords = ["test"],
            Confidence = ConfidenceLevel.High,
            ExtractedDate = "2026-04-28",
            ValidatedBy = ["test-validator"],
            RelevanceScore = 10,
            SemanticScore = 1.5,
            IsSemanticRanked = true
        };

        // Act & Assert
        skillSummary.SemanticScore.Should().Be(1.5);
        skillSummary.IsSemanticRanked.Should().BeTrue();
    }

    [Fact]
    public void SkillSummary_SemanticFields_HaveDefaults()
    {
        // Arrange
        var skillSummary = new SkillSummary
        {
            Name = "test-skill",
            Description = "Test skill description",
            Keywords = ["test"],
            Confidence = ConfidenceLevel.Medium,
            ExtractedDate = "2026-04-28",
            ValidatedBy = ["test-validator"],
            RelevanceScore = 5
            // Semantic fields not set - should have default values
        };

        // Act & Assert
        skillSummary.SemanticScore.Should().BeNull("SemanticScore should default to null");
        skillSummary.IsSemanticRanked.Should().BeFalse("IsSemanticRanked should default to false");
    }
}
