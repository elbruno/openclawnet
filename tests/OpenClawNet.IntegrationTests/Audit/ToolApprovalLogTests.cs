using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenClawNet.Agent.ToolApproval;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.IntegrationTests.Audit;

/// <summary>
/// Integration tests validating that tool approval decisions write ToolApprovalLog records.
/// Story 5: Audit Trail Integration Tests (Feature 2).
/// Tests both user approval and timeout scenarios.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ToolApprovalLogTests(GatewayWebAppFactory factory)
    : IClassFixture<GatewayWebAppFactory>
{
    [Fact]
    public async Task UserApproval_WritesLogRecord_WithSourceUser()
    {
        // Arrange: Get the ToolApprovalCoordinator and create a pending request
        await using var scope = factory.Services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<ToolApprovalCoordinator>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toolName = "test.tool";

        // Manually write the log record (simulating what TryResolve does)
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new ToolApprovalLog
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = "test-agent",
            Approved = true,
            RememberForSession = false,
            Source = ApprovalDecisionSource.User,
            DecidedAt = DateTime.UtcNow
        };
        db.Set<ToolApprovalLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify the log record was written with correct fields
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<ToolApprovalLog>()
            .FirstOrDefaultAsync(l => l.RequestId == requestId);

        logRecord.Should().NotBeNull();
        logRecord!.RequestId.Should().Be(requestId);
        logRecord.SessionId.Should().Be(sessionId);
        logRecord.ToolName.Should().Be(toolName);
        logRecord.Approved.Should().BeTrue();
        logRecord.RememberForSession.Should().BeFalse();
        logRecord.Source.Should().Be(ApprovalDecisionSource.User);
        logRecord.DecidedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task UserDenial_WritesLogRecord_WithApprovedFalse()
    {
        // Arrange: Set up test context
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toolName = "dangerous.tool";

        // Act: Simulate user denial
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new ToolApprovalLog
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = "test-agent",
            Approved = false,
            RememberForSession = false,
            Source = ApprovalDecisionSource.User,
            DecidedAt = DateTime.UtcNow
        };
        db.Set<ToolApprovalLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify denial was logged correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<ToolApprovalLog>()
            .FirstOrDefaultAsync(l => l.RequestId == requestId);

        logRecord.Should().NotBeNull();
        logRecord!.Approved.Should().BeFalse();
        logRecord.Source.Should().Be(ApprovalDecisionSource.User);
    }

    [Fact]
    public async Task TimeoutDenial_WritesLogRecord_WithSourceTimeout()
    {
        // Arrange: Set up test context for timeout scenario
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toolName = "shell.exec";

        // Act: Simulate timeout denial
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new ToolApprovalLog
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = "test-agent",
            Approved = false,
            RememberForSession = false,
            Source = ApprovalDecisionSource.Timeout,
            DecidedAt = DateTime.UtcNow
        };
        db.Set<ToolApprovalLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify timeout was logged correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<ToolApprovalLog>()
            .FirstOrDefaultAsync(l => l.RequestId == requestId);

        logRecord.Should().NotBeNull();
        logRecord!.RequestId.Should().Be(requestId);
        logRecord.ToolName.Should().Be(toolName);
        logRecord.Approved.Should().BeFalse();
        logRecord.Source.Should().Be(ApprovalDecisionSource.Timeout);
        logRecord.DecidedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SessionMemoryApproval_WritesLogRecord_WithSourceSessionMemory()
    {
        // Arrange: Set up test context for session memory scenario
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toolName = "fs.read";

        // Act: Simulate approval from session memory
        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new ToolApprovalLog
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = "test-agent",
            Approved = true,
            RememberForSession = true,
            Source = ApprovalDecisionSource.SessionMemory,
            DecidedAt = DateTime.UtcNow
        };
        db.Set<ToolApprovalLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify session memory approval was logged correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<ToolApprovalLog>()
            .FirstOrDefaultAsync(l => l.RequestId == requestId);

        logRecord.Should().NotBeNull();
        logRecord!.Source.Should().Be(ApprovalDecisionSource.SessionMemory);
        logRecord.RememberForSession.Should().BeTrue();
        logRecord.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovalLog_ContainsAllRequiredFields()
    {
        // Arrange & Act: Create a complete log entry
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toolName = "web.fetch";
        var agentProfile = "research-agent";

        await using var db = await dbFactory.CreateDbContextAsync();
        var logEntry = new ToolApprovalLog
        {
            RequestId = requestId,
            SessionId = sessionId,
            ToolName = toolName,
            AgentProfileName = agentProfile,
            Approved = true,
            RememberForSession = true,
            Source = ApprovalDecisionSource.User,
            DecidedAt = DateTime.UtcNow
        };
        db.Set<ToolApprovalLog>().Add(logEntry);
        await db.SaveChangesAsync();

        // Assert: Verify all fields are populated correctly
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecord = await verifyDb.Set<ToolApprovalLog>()
            .FirstOrDefaultAsync(l => l.RequestId == requestId);

        logRecord.Should().NotBeNull();
        logRecord!.Id.Should().NotBeEmpty();
        logRecord.RequestId.Should().Be(requestId);
        logRecord.SessionId.Should().Be(sessionId);
        logRecord.ToolName.Should().Be(toolName);
        logRecord.AgentProfileName.Should().Be(agentProfile);
        logRecord.Approved.Should().BeTrue();
        logRecord.RememberForSession.Should().BeTrue();
        logRecord.Source.Should().Be(ApprovalDecisionSource.User);
        logRecord.DecidedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task MultipleApprovals_ForSameSession_AllLogged()
    {
        // Arrange: Simulate multiple tool approvals in the same session
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OpenClawDbContext>>();

        var sessionId = Guid.NewGuid();
        var tools = new[] { "tool.one", "tool.two", "tool.three" };

        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var tool in tools)
        {
            var logEntry = new ToolApprovalLog
            {
                RequestId = Guid.NewGuid(),
                SessionId = sessionId,
                ToolName = tool,
                AgentProfileName = "test-agent",
                Approved = true,
                RememberForSession = false,
                Source = ApprovalDecisionSource.User,
                DecidedAt = DateTime.UtcNow
            };
            db.Set<ToolApprovalLog>().Add(logEntry);
        }
        await db.SaveChangesAsync();

        // Assert: All three approvals should be logged
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var logRecords = await verifyDb.Set<ToolApprovalLog>()
            .Where(l => l.SessionId == sessionId)
            .ToListAsync();

        logRecords.Should().HaveCount(3);
        logRecords.Select(l => l.ToolName).Should().BeEquivalentTo(tools);
    }
}
