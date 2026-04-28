using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using OpenClawNet.UnitTests.Fixtures;

namespace OpenClawNet.UnitTests.Storage;

public sealed class SkillVectorTests
{
    [Fact]
    public void InsertVector_WithValidData_StoresVector()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        var embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        fixture.InsertVector("skill-1", "File Reader", embedding, "Reads files from disk");

        // Assert
        var stored = fixture.GetVector("skill-1");
        stored.Should().NotBeNull();
        stored!.SkillId.Should().Be("skill-1");
        stored.SkillName.Should().Be("File Reader");
    }

    [Fact]
    public void InsertVector_WithDuplicateSkillId_ThrowsInvalidOperationException()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        var embedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        fixture.InsertVector("skill-1", "File Reader", embedding);

        // Act & Assert
        var action = () => fixture.InsertVector("skill-1", "Different Name", embedding);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpsertVector_WithExistingSkillId_UpdatesVector()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        var embedding1 = new[] { 0.1f, 0.2f, 0.3f };
        var embedding2 = new[] { 0.4f, 0.5f, 0.6f };

        fixture.UpsertVector("skill-1", "File Reader", embedding1, "Original description");

        // Act
        fixture.UpsertVector("skill-1", "File Writer", embedding2, "Updated description");

        // Assert
        var updated = fixture.GetVector("skill-1");
        updated.Should().NotBeNull();
        updated!.SkillName.Should().Be("File Writer");
    }

    [Fact]
    public void DeleteVector_WithExistingSkillId_RemovesVector()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        fixture.InsertVector("skill-1", "File Reader", new[] { 0.1f, 0.2f, 0.3f });

        // Act
        var deleted = fixture.DeleteVector("skill-1");

        // Assert
        deleted.Should().BeTrue();
        fixture.GetVector("skill-1").Should().BeNull();
    }

    [Fact]
    public void QueryByEmbedding_WithMultipleVectors_ReturnsSortedByRelevance()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        fixture.InsertVector("skill-1", "File Reader", new[] { 0.1f, 0.2f, 0.3f, 0.4f });
        fixture.InsertVector("skill-2", "File Writer", new[] { 0.2f, 0.3f, 0.4f, 0.5f });
        fixture.InsertVector("skill-3", "Database Query", new[] { 0.9f, 0.8f, 0.7f, 0.6f });

        var queryEmbedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        var results = fixture.QueryByEmbedding(queryEmbedding, topK: 3);

        // Assert
        results.Should().HaveCount(3);
        results[0].Vector.SkillId.Should().Be("skill-1");
        results[1].Vector.SkillId.Should().Be("skill-2");
    }

    [Fact]
    public void All_ReturnsAllStoredVectors()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        fixture.InsertVector("skill-1", "File Reader", new[] { 0.1f, 0.2f });
        fixture.InsertVector("skill-2", "File Writer", new[] { 0.3f, 0.4f });

        // Act
        var all = fixture.All;

        // Assert
        all.Should().HaveCount(2);
    }

    [Fact]
    public void Clear_RemovesAllVectors()
    {
        // Arrange
        var fixture = new SkillVectorFixture();
        fixture.InsertVector("skill-1", "File Reader", new[] { 0.1f, 0.2f });

        // Act
        fixture.Clear();

        // Assert
        fixture.All.Should().BeEmpty();
    }
}
