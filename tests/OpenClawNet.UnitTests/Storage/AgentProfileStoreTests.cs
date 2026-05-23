using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OpenClawNet.Models.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.UnitTests.Storage;

public class AgentProfileStoreTests : IDisposable
{
    private readonly IDbContextFactory<OpenClawDbContext> _factory;

    public AgentProfileStoreTests()
    {
        var options = new DbContextOptionsBuilder<OpenClawDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsProfile()
    {
        var store = new AgentProfileStore(_factory);
        var profile = new AgentProfile
        {
            Name = "test-agent",
            DisplayName = "Test Agent",
            Provider = "ollama",
            RetrievalLevel = RetrievalLevel.Hybrid
        };

        await store.SaveAsync(profile);
        var result = await store.GetAsync("test-agent");

        result.Should().NotBeNull();
        result!.Name.Should().Be("test-agent");
        result.DisplayName.Should().Be("Test Agent");
        result.Provider.Should().Be("ollama");
        result.RetrievalLevel.Should().Be(RetrievalLevel.Hybrid);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        var store = new AgentProfileStore(_factory);

        var result = await store.GetAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsAllProfiles_OrderedByName()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "beta" });
        await store.SaveAsync(new AgentProfile { Name = "alpha" });

        var results = await store.ListAsync();

        results.Should().HaveCount(2);
        results[0].Name.Should().Be("alpha");
        results[1].Name.Should().Be("beta");
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfile()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "delete-me" });

        await store.DeleteAsync("delete-me");

        var result = await store.GetAsync("delete-me");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultAsync_SeedsDefault_WhenNoneExists()
    {
        var store = new AgentProfileStore(_factory);

        var result = await store.GetDefaultAsync();

        result.Should().NotBeNull();
        result.Name.Should().Be("openclawnet-agent");
        result.IsDefault.Should().BeTrue();
        result.Instructions.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDefaultAsync_ReturnsExistingDefault()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile
        {
            Name = "my-default",
            IsDefault = true,
            Instructions = "Custom default instructions."
        });

        var result = await store.GetDefaultAsync();

        result.Name.Should().Be("my-default");
        result.Instructions.Should().Be("Custom default instructions.");
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingProfile()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "update-me", DisplayName = "v1" });

        await store.SaveAsync(new AgentProfile { Name = "update-me", DisplayName = "v2" });

        var result = await store.GetAsync("update-me");
        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("v2");
    }

    [Fact]
    public void NewAgentProfile_RequireToolApproval_DefaultsToTrue()
    {
        // Wave 4 PR-1: safe-by-default per Bruno's directive 2026-04-19.
        var profile = new AgentProfile { Name = "fresh" };

        profile.RequireToolApproval.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_RequireToolApproval_WhenDisabled()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile
        {
            Name = "cron-bot",
            RequireToolApproval = false
        });

        var result = await store.GetAsync("cron-bot");

        result.Should().NotBeNull();
        result!.RequireToolApproval.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_RequireToolApproval_WhenEnabled()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile
        {
            Name = "interactive",
            RequireToolApproval = true
        });

        var result = await store.GetAsync("interactive");

        result.Should().NotBeNull();
        result!.RequireToolApproval.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ThenUpdate_TogglesRequireToolApproval()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "toggle", RequireToolApproval = true });

        await store.SaveAsync(new AgentProfile { Name = "toggle", RequireToolApproval = false });

        var result = await store.GetAsync("toggle");
        result!.RequireToolApproval.Should().BeFalse();
    }

    public void Dispose()
    {
        // InMemory databases are automatically cleaned up
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_Kind()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "tt", Kind = ProfileKind.ToolTester });
        await store.SaveAsync(new AgentProfile { Name = "sys", Kind = ProfileKind.System });

        var tt = await store.GetAsync("tt");
        var sys = await store.GetAsync("sys");

        tt!.Kind.Should().Be(ProfileKind.ToolTester);
        sys!.Kind.Should().Be(ProfileKind.System);
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_RetrievalLevel()
    {
        var store = new AgentProfileStore(_factory);
        await store.SaveAsync(new AgentProfile { Name = "retriever", RetrievalLevel = RetrievalLevel.VectorDb });

        var result = await store.GetAsync("retriever");

        result.Should().NotBeNull();
        result!.RetrievalLevel.Should().Be(RetrievalLevel.VectorDb);
    }

    [Fact]
    public async Task GetDefaultAsync_IgnoresNonStandard_EvenWhenIsDefaultTrue()
    {
        var store = new AgentProfileStore(_factory);
        // Save standard first (IsDefault=true). Then save tool-tester second with IsDefault=false
        // to keep the standard as the only default candidate. Non-standard kinds must never win.
        await store.SaveAsync(new AgentProfile { Name = "std", Kind = ProfileKind.Standard, IsDefault = true });
        await store.SaveAsync(new AgentProfile { Name = "tt-default", Kind = ProfileKind.ToolTester, IsDefault = false });

        var result = await store.GetDefaultAsync();
        result.Should().NotBeNull();
        result!.Name.Should().Be("std");
        result.Kind.Should().Be(ProfileKind.Standard);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;
        public TestDbContextFactory(DbContextOptions<OpenClawDbContext> options) => _options = options;
        public OpenClawDbContext CreateDbContext() => new(_options);
    }
}
