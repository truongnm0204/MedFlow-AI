using MedFlow.Application.Services.Visits.Queueing;

namespace MedFlow.UnitTests.Visits;

public sealed class ExaminationQueueOrderingEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0);
    private static readonly QueueOrderingPolicy DefaultPolicy = new(
        GraceMinutes: 15,
        ExpectedDurationMinutes: 15,
        SafetyBufferMinutes: 5);

    [Fact]
    public void Evaluate_Scenario1_SingleWalkInNotArrived_IsIneligibleAndCannotSafeBackfill()
    {
        var items = new[]
        {
            new QueueOrderingItem(
                Status: "WAITING",
                Priority: "NORMAL",
                PresenceStatus: "NOT_ARRIVED",
                IsScheduled: false,
                StoredArrivalClass: "WALK_IN",
                ScheduledAt: null,
                ArrivedAt: null,
                ReadySince: Now.AddMinutes(-10),
                BackfilledAt: null,
                QueueNumber: 1),
        };

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now);
        var eval = Assert.Single(result.Evaluations);

        Assert.False(eval.IsEligible);
        Assert.False(eval.CanSafeBackfill);
        Assert.NotNull(eval.HoldReason);
        Assert.Contains("Có mặt", eval.HoldReason);
    }

    [Fact]
    public void Evaluate_Scenario2_DueProtectedAppointmentBlocksEarlyArrivalSafeBackfill()
    {
        var p = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 8, 45, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 40, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 40, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var e = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 10, 0, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 30, 0),
            BackfilledAt: null,
            QueueNumber: 2);

        var result = ExaminationQueueOrderingEngine.Evaluate([p, e], DefaultPolicy, Now);

        var evalP = result.Evaluations[0];
        var evalE = result.Evaluations[1];

        Assert.True(evalP.IsDueProtected);
        Assert.Equal(1, evalP.ServingOrder);

        Assert.Equal("EARLY", evalE.EffectiveArrivalClass);
        Assert.False(evalE.CanSafeBackfill);
        Assert.NotNull(evalE.HoldReason);
        Assert.Contains("đúng lịch", evalE.HoldReason);
        Assert.Equal(2, evalE.ServingOrder);
    }

    [Fact]
    public void Evaluate_Scenario3_RoomBusyBlocksSafeBackfill()
    {
        var inProgress = new QueueOrderingItem(
            Status: "IN_PROGRESS",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: new DateTime(2026, 10, 22, 8, 15, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 15, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var early = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "EARLY",
            ScheduledAt: new DateTime(2026, 10, 22, 10, 0, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 30, 0),
            BackfilledAt: null,
            QueueNumber: 2);

        var result = ExaminationQueueOrderingEngine.Evaluate([inProgress, early], DefaultPolicy, Now);
        var evalEarly = result.Evaluations[1];

        Assert.False(evalEarly.CanSafeBackfill);
        Assert.NotNull(evalEarly.HoldReason);
        Assert.Contains("Phòng khám đang có", evalEarly.HoldReason);
    }

    [Fact]
    public void Evaluate_Scenario4_UpcomingProtectedAppointmentWindowCheck()
    {
        var early = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "EARLY",
            ScheduledAt: new DateTime(2026, 10, 22, 10, 30, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 30, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var reserved0915 = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "NOT_ARRIVED",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 9, 15, 0),
            ArrivedAt: null,
            ReadySince: new DateTime(2026, 10, 22, 9, 15, 0),
            BackfilledAt: null,
            QueueNumber: 2);

        var blockedResult = ExaminationQueueOrderingEngine.Evaluate([early, reserved0915], DefaultPolicy, Now);
        Assert.False(blockedResult.Evaluations[0].CanSafeBackfill);
        Assert.NotNull(blockedResult.Evaluations[0].HoldReason);
        Assert.Contains("09:15", blockedResult.Evaluations[0].HoldReason);

        var reserved0930 = reserved0915 with
        {
            ScheduledAt = new DateTime(2026, 10, 22, 9, 30, 0),
            ReadySince = new DateTime(2026, 10, 22, 9, 30, 0),
        };

        var allowedResult = ExaminationQueueOrderingEngine.Evaluate([early, reserved0930], DefaultPolicy, Now);
        Assert.True(allowedResult.Evaluations[0].CanSafeBackfill);
        Assert.Null(allowedResult.Evaluations[0].HoldReason);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 30, 0), allowedResult.Evaluations[0].ProtectedNextStartsAt);
    }

    [Fact]
    public void Evaluate_Scenario5_NullExpectedDurationWithAndWithoutProtectedAppointment()
    {
        var policyWithoutDuration = DefaultPolicy with { ExpectedDurationMinutes = null };

        var early = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "EARLY",
            ScheduledAt: new DateTime(2026, 10, 22, 10, 30, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 30, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var reserved0930 = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "NOT_ARRIVED",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 9, 30, 0),
            ArrivedAt: null,
            ReadySince: new DateTime(2026, 10, 22, 9, 30, 0),
            BackfilledAt: null,
            QueueNumber: 2);

        var withProtected = ExaminationQueueOrderingEngine.Evaluate([early, reserved0930], policyWithoutDuration, Now);
        Assert.False(withProtected.Evaluations[0].CanSafeBackfill);
        Assert.NotNull(withProtected.Evaluations[0].HoldReason);
        Assert.Contains("Chưa xác định", withProtected.Evaluations[0].HoldReason);

        var withoutProtected = ExaminationQueueOrderingEngine.Evaluate([early], policyWithoutDuration, Now);
        Assert.True(withoutProtected.Evaluations[0].CanSafeBackfill);
        Assert.Null(withoutProtected.Evaluations[0].HoldReason);
    }

    [Fact]
    public void Evaluate_Scenario7_LateArrivalLosesAppointmentProtection()
    {
        var lateItem = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 8, 0, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 30, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var result = ExaminationQueueOrderingEngine.Evaluate([lateItem], DefaultPolicy, Now);
        var eval = Assert.Single(result.Evaluations);

        Assert.Equal("LATE", eval.EffectiveArrivalClass);
        Assert.False(eval.IsAppointmentProtected);
        Assert.False(eval.IsDueProtected);
    }

    [Fact]
    public void Evaluate_Scenario10_PreservesQueueNumbersAndAssignsPermutationServingOrders()
    {
        var items = new[]
        {
            new QueueOrderingItem("COMPLETED", "NORMAL", "PRESENT", false, "WALK_IN", null, Now.AddMinutes(-60), Now.AddMinutes(-60), null, QueueNumber: 10),
            new QueueOrderingItem("WAITING", "NORMAL", "PRESENT", false, "WALK_IN", null, Now.AddMinutes(-20), Now.AddMinutes(-20), null, QueueNumber: 25),
            new QueueOrderingItem("IN_PROGRESS", "NORMAL", "PRESENT", false, "WALK_IN", null, Now.AddMinutes(-40), Now.AddMinutes(-40), null, QueueNumber: 15),
            new QueueOrderingItem("SKIPPED", "NORMAL", "LEFT", false, "WALK_IN", null, Now.AddMinutes(-50), Now.AddMinutes(-50), null, QueueNumber: 12),
        };

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now);

        Assert.Equal([10, 25, 15, 12], items.Select(x => x.QueueNumber).ToArray());
        Assert.Equal([1, 2, 3, 4], result.Evaluations.Select(e => e.ServingOrder).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Evaluate_Ordering_SortsDueProtectedThenBackfilledReadyThenPriorityAndFifo()
    {
        var normalEarlyReady = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: new DateTime(2026, 10, 22, 8, 0, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 0, 0),
            BackfilledAt: null,
            QueueNumber: 1);

        var legacyUrgentLaterReady = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "URGENT",
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: new DateTime(2026, 10, 22, 8, 10, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 10, 0),
            BackfilledAt: null,
            QueueNumber: 2);

        var elderlyWaiting = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "ELDERLY",
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: new DateTime(2026, 10, 22, 8, 20, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 20, 0),
            BackfilledAt: null,
            QueueNumber: 3);

        var backfilledReady = new QueueOrderingItem(
            Status: "READY",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "EARLY",
            ScheduledAt: new DateTime(2026, 10, 22, 10, 0, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 30, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 45, 0),
            BackfilledAt: new DateTime(2026, 10, 22, 8, 45, 0),
            QueueNumber: 4);

        var dueProtected = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "ON_TIME_OR_GRACE",
            ScheduledAt: new DateTime(2026, 10, 22, 8, 50, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 48, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 48, 0),
            BackfilledAt: null,
            QueueNumber: 5);

        var items = new[]
        {
            normalEarlyReady,      // idx 0 -> tier 2, priority 2, ready 08:00 -> order 4
            legacyUrgentLaterReady,// idx 1 -> tier 2, priority 2, ready 08:10 -> order 5
            elderlyWaiting,        // idx 2 -> tier 2, priority 0, ready 08:20 -> order 3
            backfilledReady,       // idx 3 -> tier 1 -> order 2
            dueProtected,          // idx 4 -> tier 0 -> order 1
        };

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now);

        Assert.Equal([4, 3, 2, 0, 1], result.OrderedIndices.ToArray());
        Assert.Equal(1, result.Evaluations[4].ServingOrder);
        Assert.Equal(2, result.Evaluations[3].ServingOrder);
        Assert.Equal(3, result.Evaluations[2].ServingOrder);
        Assert.Equal(4, result.Evaluations[0].ServingOrder);
        Assert.Equal(5, result.Evaluations[1].ServingOrder);
    }
}
