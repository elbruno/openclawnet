using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;
using Xunit;

namespace OpenClawNet.UnitTests.Agent;

public sealed class ToolApprovalAuditorTests
{
    [Fact]
    public async Task RecordAsync_PersistsRow_WithAllFields()
    {
        await using var factory = new InMemoryDbContextFactory();
        var auditor = new ToolApprovalAuditor(factory, NullLogger<ToolApprovalAuditor>.Instance);

        var sessionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await auditor.RecordAsync(new ToolApprovalAuditEntry(
            RequestId: requestId,
            SessionId: sessionId,
            ToolName: "shell.exec",
            AgentProfileName: "demo-profile",
            Approved: true,
            RememberForSession: true,
            Source: ToolApprovalAuditSource.User));

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.ToolApprovalLogs.SingleAsync();
        row.RequestId.Should().Be(requestId);
        row.SessionId.Should().Be(sessionId);
        row.ToolName.Should().Be("shell.exec");
        row.AgentProfileName.Should().Be("demo-profile");
        row.Approved.Should().BeTrue();
        row.RememberForSession.Should().BeTrue();
        row.Source.Should().Be(ApprovalDecisionSource.User);
    }

    [Fact]
    public async Task RecordAsync_SwallowsExceptions_OnPersistenceFailure()
    {
        var auditor = new ToolApprovalAuditor(new ThrowingDbContextFactory(), NullLogger<ToolApprovalAuditor>.Instance);

        // Must not throw — best-effort contract.
        var act = async () => await auditor.RecordAsync(new ToolApprovalAuditEntry(
            Guid.NewGuid(), Guid.NewGuid(), "x", null, false, false, ToolApprovalAuditSource.Timeout));

        await act.Should().NotThrowAsync();
    }

    private sealed class InMemoryDbContextFactory : IDbContextFactory<OpenClawDbContext>, IAsyncDisposable
    {
        private readonly DbContextOptions<OpenClawDbContext> _options;

        public InMemoryDbContextFactory()
        {
            _options = new DbContextOptionsBuilder<OpenClawDbContext>()
                .UseInMemoryDatabase("audit-" + Guid.NewGuid())
                .Options;
        }

        public OpenClawDbContext CreateDbContext() => new(_options);

        public Task<OpenClawDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new OpenClawDbContext(_options));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<OpenClawDbContext>
    {
        public OpenClawDbContext CreateDbContext() => throw new InvalidOperationException("boom");
        public Task<OpenClawDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("boom-async");
    }
}
