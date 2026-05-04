using FluentAssertions;
using OpenClawNet.Storage;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Storage;

public sealed class JobStatusTransitionsTests
{
    // ──────────────────────────────────────────────
    // Enum value verification
    // ──────────────────────────────────────────────

    [Fact]
    public void JobStatus_Draft_HasValue0()
    {
        ((int)JobStatus.Draft).Should().Be(0);
    }

    [Fact]
    public void JobStatus_Active_HasValue1()
    {
        ((int)JobStatus.Active).Should().Be(1);
    }

    [Fact]
    public void JobStatus_Paused_HasValue2()
    {
        ((int)JobStatus.Paused).Should().Be(2);
    }

    [Fact]
    public void JobStatus_Cancelled_HasValue3()
    {
        ((int)JobStatus.Cancelled).Should().Be(3);
    }

    [Fact]
    public void JobStatus_Completed_HasValue4()
    {
        ((int)JobStatus.Completed).Should().Be(4);
    }

    [Fact]
    public void JobStatus_HasExactlySixValues()
    {
        Enum.GetValues<JobStatus>().Should().HaveCount(6);
    }

    // ──────────────────────────────────────────────
    // Valid transitions (should return true)
    // ──────────────────────────────────────────────

    [Fact]
    public void IsAllowed_DraftToActive_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Draft, JobStatus.Active)
            .Should().BeTrue("Draft → Active is the 'start' transition");
    }

    [Fact]
    public void IsAllowed_DraftToCancelled_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Draft, JobStatus.Cancelled)
            .Should().BeTrue("Draft → Cancelled is the 'cancel before start' transition");
    }

    [Fact]
    public void IsAllowed_ActiveToPaused_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Active, JobStatus.Paused)
            .Should().BeTrue("Active → Paused is the 'pause' transition");
    }

    [Fact]
    public void IsAllowed_ActiveToCancelled_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Active, JobStatus.Cancelled)
            .Should().BeTrue("Active → Cancelled is the 'cancel while running' transition");
    }

    [Fact]
    public void IsAllowed_ActiveToCompleted_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Active, JobStatus.Completed)
            .Should().BeTrue("Active → Completed is the 'scheduler completes' transition");
    }

    [Fact]
    public void IsAllowed_PausedToActive_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Paused, JobStatus.Active)
            .Should().BeTrue("Paused → Active is the 'resume' transition");
    }

    [Fact]
    public void IsAllowed_PausedToCancelled_ReturnsTrue()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Paused, JobStatus.Cancelled)
            .Should().BeTrue("Paused → Cancelled is the 'cancel while paused' transition");
    }

    [Theory]
    [InlineData(JobStatus.Draft, JobStatus.Active)]
    [InlineData(JobStatus.Draft, JobStatus.Cancelled)]
    [InlineData(JobStatus.Active, JobStatus.Paused)]
    [InlineData(JobStatus.Active, JobStatus.Cancelled)]
    [InlineData(JobStatus.Active, JobStatus.Completed)]
    [InlineData(JobStatus.Paused, JobStatus.Active)]
    [InlineData(JobStatus.Paused, JobStatus.Cancelled)]
    public void IsAllowed_AllValidTransitions_ReturnTrue(JobStatus from, JobStatus to)
    {
        JobStatusTransitions.IsAllowed(from, to).Should().BeTrue(
            $"{from} → {to} should be a valid transition");
    }

    // ──────────────────────────────────────────────
    // Invalid transitions (should return false)
    // ──────────────────────────────────────────────

    [Fact]
    public void IsAllowed_DraftToPaused_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Draft, JobStatus.Paused)
            .Should().BeFalse("must start before pausing");
    }

    [Fact]
    public void IsAllowed_DraftToCompleted_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Draft, JobStatus.Completed)
            .Should().BeFalse("can't complete without running");
    }

    [Fact]
    public void IsAllowed_ActiveToDraft_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Active, JobStatus.Draft)
            .Should().BeFalse("can't go back to Draft");
    }

    [Fact]
    public void IsAllowed_PausedToCompleted_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Paused, JobStatus.Completed)
            .Should().BeFalse("only scheduler completes from Active");
    }

    [Fact]
    public void IsAllowed_PausedToDraft_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Paused, JobStatus.Draft)
            .Should().BeFalse("can't go back to Draft");
    }

    [Fact]
    public void IsAllowed_CancelledToActive_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Cancelled, JobStatus.Active)
            .Should().BeFalse("Cancelled is a terminal state");
    }

    [Fact]
    public void IsAllowed_CancelledToDraft_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Cancelled, JobStatus.Draft)
            .Should().BeFalse("Cancelled is a terminal state");
    }

    [Fact]
    public void IsAllowed_CompletedToActive_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Completed, JobStatus.Active)
            .Should().BeFalse("Completed is a terminal state");
    }

    [Fact]
    public void IsAllowed_CompletedToDraft_ReturnsFalse()
    {
        JobStatusTransitions.IsAllowed(JobStatus.Completed, JobStatus.Draft)
            .Should().BeFalse("Completed is a terminal state");
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Completed)]
    public void IsAllowed_SameStateToSameState_ReturnsFalse(JobStatus status)
    {
        JobStatusTransitions.IsAllowed(status, status)
            .Should().BeFalse($"self-transition {status} → {status} should never be allowed");
    }

    [Theory]
    [InlineData(JobStatus.Draft, JobStatus.Paused)]
    [InlineData(JobStatus.Draft, JobStatus.Completed)]
    [InlineData(JobStatus.Active, JobStatus.Draft)]
    [InlineData(JobStatus.Paused, JobStatus.Completed)]
    [InlineData(JobStatus.Paused, JobStatus.Draft)]
    [InlineData(JobStatus.Cancelled, JobStatus.Active)]
    [InlineData(JobStatus.Cancelled, JobStatus.Draft)]
    [InlineData(JobStatus.Cancelled, JobStatus.Paused)]
    [InlineData(JobStatus.Cancelled, JobStatus.Completed)]
    [InlineData(JobStatus.Completed, JobStatus.Active)]
    [InlineData(JobStatus.Completed, JobStatus.Draft)]
    [InlineData(JobStatus.Completed, JobStatus.Paused)]
    [InlineData(JobStatus.Completed, JobStatus.Cancelled)]
    public void IsAllowed_AllInvalidTransitions_ReturnFalse(JobStatus from, JobStatus to)
    {
        JobStatusTransitions.IsAllowed(from, to).Should().BeFalse(
            $"{from} → {to} should not be a valid transition");
    }

    // ──────────────────────────────────────────────
    // Terminal state detection
    // ──────────────────────────────────────────────

    [Fact]
    public void IsTerminal_Completed_ReturnsTrue()
    {
        JobStatusTransitions.IsTerminal(JobStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_Cancelled_ReturnsTrue()
    {
        JobStatusTransitions.IsTerminal(JobStatus.Cancelled).Should().BeTrue();
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    public void IsTerminal_NonTerminalStates_ReturnFalse(JobStatus status)
    {
        JobStatusTransitions.IsTerminal(status).Should().BeFalse(
            $"{status} is not a terminal state");
    }

    // ──────────────────────────────────────────────
    // Editability checks
    // ──────────────────────────────────────────────

    [Fact]
    public void IsEditable_Draft_ReturnsTrue()
    {
        JobStatusTransitions.IsEditable(JobStatus.Draft).Should().BeTrue();
    }

    [Fact]
    public void IsEditable_Paused_ReturnsTrue()
    {
        JobStatusTransitions.IsEditable(JobStatus.Paused).Should().BeTrue();
    }

    [Theory]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public void IsEditable_NonEditableStates_ReturnFalse(JobStatus status)
    {
        JobStatusTransitions.IsEditable(status).Should().BeFalse(
            $"{status} should not be editable");
    }

    // ──────────────────────────────────────────────
    // Edge cases: no transitions out of terminal states
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Completed)]
    public void IsAllowed_CancelledToAnyState_ReturnsFalse(JobStatus to)
    {
        JobStatusTransitions.IsAllowed(JobStatus.Cancelled, to)
            .Should().BeFalse("no transitions allowed from Cancelled");
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Active)]
    [InlineData(JobStatus.Paused)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Completed)]
    public void IsAllowed_CompletedToAnyState_ReturnsFalse(JobStatus to)
    {
        JobStatusTransitions.IsAllowed(JobStatus.Completed, to)
            .Should().BeFalse("no transitions allowed from Completed");
    }
}
