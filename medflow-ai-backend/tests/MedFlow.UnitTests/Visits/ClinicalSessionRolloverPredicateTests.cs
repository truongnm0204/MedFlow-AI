using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;

namespace MedFlow.UnitTests.Visits;

/// <summary>
/// Commit A (PR #859 / F01) rollover predicate: prior-day rollover may only close unstarted tickets.
/// Active execution (IN_PROGRESS / IN_CONSULTATION / SERVING) and intermediate CALLED are never
/// auto-deferred because of QueueDate, for any queue type (decision doc §4.1 / RCX-01..RCX-06).
/// </summary>
public sealed class ClinicalSessionRolloverPredicateTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);
    private static readonly DateTime Now = new(2026, 11, 27, 9, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void CloseStalePriorDayQueuesOnVisit_DefersOnlyWaitingAndReadyTickets()
    {
        var queues = new List<QueueEntry>
        {
            Queue("RETURN_CONSULTATION", "WAITING"),
            Queue("RETURN_CONSULTATION", "READY"),
            Queue("EXAMINATION", "WAITING"),
            Queue("DIAGNOSTIC", "READY"),
        };

        var closed = PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(queues, Today, Now);

        Assert.Equal(4, closed);
        Assert.All(queues, q =>
        {
            Assert.Equal("DEFERRED", q.Status);
            Assert.Equal(Now, q.CompletedAt);
            Assert.Equal(2, q.StateVersion);
        });
    }

    [Theory]
    [InlineData("EXAMINATION")]
    [InlineData("RETURN_CONSULTATION")]
    [InlineData("DIAGNOSTIC")]
    [InlineData("PHARMACY")]
    public void CloseStalePriorDayQueuesOnVisit_NeverAutoDefersActiveExecution(string queueType)
    {
        foreach (var status in new[] { "IN_PROGRESS", "IN_CONSULTATION", "SERVING" })
        {
            var queue = Queue(queueType, status);
            var closed = PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit([queue], Today, Now);

            Assert.Equal(0, closed);
            Assert.Equal(status, queue.Status);
            Assert.Null(queue.CompletedAt);
            Assert.Equal(1, queue.StateVersion);
        }
    }

    [Fact]
    public void CloseStalePriorDayQueuesOnVisit_NeverAutoDefersCalledOrTerminalTickets()
    {
        var called = Queue("RETURN_CONSULTATION", "CALLED");
        var completed = Queue("EXAMINATION", "COMPLETED");
        var cancelled = Queue("RETURN_CONSULTATION", "CANCELLED");

        var closed = PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(
            [called, completed, cancelled], Today, Now);

        Assert.Equal(0, closed);
        Assert.Equal("CALLED", called.Status);
        Assert.Null(called.CompletedAt);
        Assert.Equal("COMPLETED", completed.Status);
        Assert.Equal("CANCELLED", cancelled.Status);
    }

    [Fact]
    public void CloseStalePriorDayQueuesOnVisit_DoesNotTouchSameDayTickets()
    {
        var queue = Queue("RETURN_CONSULTATION", "WAITING");
        queue.QueueDate = Today;

        var closed = PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit([queue], Today, Now);

        Assert.Equal(0, closed);
        Assert.Equal("WAITING", queue.Status);
        Assert.Null(queue.CompletedAt);
    }

    private static QueueEntry Queue(string queueType, string status) => new()
    {
        Id = 1,
        VisitId = 10,
        QueueDate = Today.AddDays(-1),
        QueueType = queueType,
        Status = status,
        StateVersion = 1,
        PresenceStatus = "PRESENT",
    };
}
