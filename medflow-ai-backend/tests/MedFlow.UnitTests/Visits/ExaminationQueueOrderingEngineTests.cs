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
        Assert.Equal("NOT_ARRIVED", eval.IneligibleReason);
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

    private static QueueOrderingItem[] CreateOrderingScenario()
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

        return
        [
            normalEarlyReady,       // idx 0
            legacyUrgentLaterReady, // idx 1 (legacy URGENT is treated as NORMAL)
            elderlyWaiting,         // idx 2
            backfilledReady,        // idx 3 (next protected appointment, not due yet)
            dueProtected,           // idx 4
        ];
    }

    [Fact]
    public void Evaluate_Ordering_DueProtectedFirstThenGapSafeElderlyThenBackfilledThenFifo()
    {
        var items = CreateOrderingScenario();

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now);

        // idx 4 -> tier 0 (due protected appointment 08:50)
        // idx 2 -> tier 1 (elderly: 09:00 + 15 + 5 <= next protected 10:00)
        // idx 3 -> tier 3 (safe backfilled READY)
        // idx 0, idx 1 -> tier 4 FIFO by ready time (08:00, 08:10)
        Assert.Equal([4, 2, 3, 0, 1], result.OrderedIndices.ToArray());
        Assert.Equal(1, result.Evaluations[4].ServingOrder);
        Assert.Equal(2, result.Evaluations[2].ServingOrder);
        Assert.Equal(3, result.Evaluations[3].ServingOrder);
        Assert.Equal(4, result.Evaluations[0].ServingOrder);
        Assert.Equal(5, result.Evaluations[1].ServingOrder);
    }

    [Fact]
    public void Evaluate_Ordering_ElderlyLosesFastTrackWhenGapBeforeNextProtectedIsTooShort()
    {
        var items = CreateOrderingScenario();
        items[3] = items[3] with { ScheduledAt = new DateTime(2026, 10, 22, 9, 15, 0) };

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now);

        // 09:00 + 15 + 5 > 09:15 -> elderly falls back to FIFO (ready 08:20) behind the backfilled entry.
        Assert.Equal([4, 3, 0, 1, 2], result.OrderedIndices.ToArray());
    }

    [Fact]
    public void Evaluate_ReturnConsultation_PendingResultsIsIneligibleUntilReady()
    {
        var returnPending = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: new DateTime(2026, 10, 22, 8, 40, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 40, 0),
            BackfilledAt: null,
            QueueNumber: 2,
            QueueType: "RETURN_CONSULTATION",
            PendingRequiredResults: true);

        var pending = Assert.Single(ExaminationQueueOrderingEngine.Evaluate([returnPending], DefaultPolicy, Now).Evaluations);
        Assert.False(pending.IsEligible);
        Assert.Equal("AWAITING_RESULTS", pending.IneligibleReason);
        Assert.False(pending.CanSafeBackfill);

        var notRequired = Assert.Single(ExaminationQueueOrderingEngine.Evaluate(
            [returnPending],
            DefaultPolicy with { FinalResultRequired = false },
            Now).Evaluations);
        Assert.True(notRequired.IsEligible);
        Assert.Null(notRequired.IneligibleReason);

        var earlierWalkIn = new QueueOrderingItem(
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
        var returnReady = returnPending with { PendingRequiredResults = false };

        var ordered = ExaminationQueueOrderingEngine.Evaluate([earlierWalkIn, returnReady], DefaultPolicy, Now);
        Assert.Equal([1, 0], ordered.OrderedIndices.ToArray());
        Assert.True(ordered.Evaluations[1].IsEligible);
    }

    [Fact]
    public void Evaluate_BlockedReason_BlocksUntilScheduledTimeThenRestoresDueProtection()
    {
        var held = new QueueOrderingItem(
            Status: "WAITING",
            Priority: "NORMAL",
            PresenceStatus: "PRESENT",
            IsScheduled: true,
            StoredArrivalClass: "EARLY",
            ScheduledAt: new DateTime(2026, 10, 22, 9, 30, 0),
            ArrivedAt: new DateTime(2026, 10, 22, 8, 40, 0),
            ReadySince: new DateTime(2026, 10, 22, 8, 40, 0),
            BackfilledAt: null,
            QueueNumber: 1,
            BlockedReason: "Giữ chờ tới khung giờ hẹn 09:30");

        var beforeSlot = Assert.Single(ExaminationQueueOrderingEngine.Evaluate([held], DefaultPolicy, Now).Evaluations);
        Assert.False(beforeSlot.IsEligible);
        Assert.Equal("BLOCKED", beforeSlot.IneligibleReason);
        Assert.False(beforeSlot.IsDueProtected);

        var atSlot = Assert.Single(ExaminationQueueOrderingEngine.Evaluate([held], DefaultPolicy, Now.AddMinutes(30)).Evaluations);
        Assert.True(atSlot.IsEligible);
        Assert.True(atSlot.IsDueProtected);
        Assert.Null(atSlot.IneligibleReason);

        var blockedWalkIn = held with
        {
            IsScheduled = false,
            StoredArrivalClass = "WALK_IN",
            ScheduledAt = null,
        };
        var walkIn = Assert.Single(ExaminationQueueOrderingEngine.Evaluate([blockedWalkIn], DefaultPolicy, Now.AddMinutes(30)).Evaluations);
        Assert.False(walkIn.IsEligible);
        Assert.Equal("BLOCKED", walkIn.IneligibleReason);
    }

    [Fact]
    public void Evaluate_FairnessRotation_InterleavesFiveElderlyAndTwoNormalAt3To1Ratio()
    {
        var items = new[]
        {
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-50), queueNumber: 1), // idx 0: E1
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-45), queueNumber: 2), // idx 1: E2
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-40), queueNumber: 3), // idx 2: E3
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-35), queueNumber: 4), // idx 3: E4
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-30), queueNumber: 5), // idx 4: E5
            CreateWalkInItem("NORMAL", Now.AddMinutes(-55), queueNumber: 6),  // idx 5: N1
            CreateWalkInItem("NORMAL", Now.AddMinutes(-25), queueNumber: 7),  // idx 6: N2
        };

        var fromZero = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now, consecutivePriorityCalls: 0);

        // Expected order: E1(0), E2(1), E3(2), N1(5), E4(3), E5(4), N2(6)
        Assert.Equal([0, 1, 2, 5, 3, 4, 6], fromZero.OrderedIndices.ToArray());
        Assert.Equal(1, fromZero.Evaluations[0].ServingOrder);
        Assert.Equal(2, fromZero.Evaluations[1].ServingOrder);
        Assert.Equal(3, fromZero.Evaluations[2].ServingOrder);
        Assert.Equal(4, fromZero.Evaluations[5].ServingOrder);
        Assert.Equal(5, fromZero.Evaluations[3].ServingOrder);
        Assert.Equal(6, fromZero.Evaluations[4].ServingOrder);
        Assert.Equal(7, fromZero.Evaluations[6].ServingOrder);

        var fromThree = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now, consecutivePriorityCalls: 3);

        // With counter=3, N1(5) is placed at the head of the eligible slice: N1(5), E1(0), E2(1), E3(2), N2(6), E4(3), E5(4)
        Assert.Equal([5, 0, 1, 2, 6, 3, 4], fromThree.OrderedIndices.ToArray());
        Assert.Equal(1, fromThree.Evaluations[5].ServingOrder);
    }

    [Fact]
    public void Evaluate_FairnessRotation_OnlyElderlyWithCounterThree_KeepsSequentialOrderWithoutGaps()
    {
        var items = new[]
        {
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-30), queueNumber: 1),
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-20), queueNumber: 2),
            CreateWalkInItem("ELDERLY", Now.AddMinutes(-10), queueNumber: 3),
        };

        var result = ExaminationQueueOrderingEngine.Evaluate(items, DefaultPolicy, Now, consecutivePriorityCalls: 3);

        Assert.Equal([0, 1, 2], result.OrderedIndices.ToArray());
        Assert.Equal([1, 2, 3], result.Evaluations.Select(e => e.ServingOrder).ToArray());
    }

    private static QueueOrderingItem CreateWalkInItem(string priority, DateTime readySince, int queueNumber)
        => new(
            Status: "WAITING",
            Priority: priority,
            PresenceStatus: "PRESENT",
            IsScheduled: false,
            StoredArrivalClass: "WALK_IN",
            ScheduledAt: null,
            ArrivedAt: readySince,
            ReadySince: readySince,
            BackfilledAt: null,
            QueueNumber: queueNumber);
}
