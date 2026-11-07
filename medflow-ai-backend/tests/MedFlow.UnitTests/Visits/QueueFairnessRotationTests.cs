using MedFlow.Application.Services.Visits.Queueing;

namespace MedFlow.UnitTests.Visits;

public sealed class QueueFairnessRotationTests
{
    [Fact]
    public void PreferPriorityAndAdvance_Ratio3To1FromZero_ProducesExpectedCycle()
    {
        var picks = new List<string>(8);
        var counter = 0;

        for (var i = 0; i < 8; i++)
        {
            var preferPriority = QueueFairnessRotation.PreferPriority(counter, priorityWeight: 3, normalWeight: 1);
            picks.Add(preferPriority ? "P" : "N");
            counter = QueueFairnessRotation.Advance(counter, preferPriority, priorityWeight: 3, normalWeight: 1);
        }

        Assert.Equal(["P", "P", "P", "N", "P", "P", "P", "N"], picks);
    }

    [Fact]
    public void PreferPriorityAndAdvance_Ratio2To2FromZero_ProducesExpectedCycle()
    {
        var picks = new List<string>(5);
        var counter = 0;

        for (var i = 0; i < 5; i++)
        {
            var preferPriority = QueueFairnessRotation.PreferPriority(counter, priorityWeight: 2, normalWeight: 2);
            picks.Add(preferPriority ? "P" : "N");
            counter = QueueFairnessRotation.Advance(counter, preferPriority, priorityWeight: 2, normalWeight: 2);
        }

        Assert.Equal(["P", "P", "N", "N", "P"], picks);
    }

    [Fact]
    public void Advance_ClampsAtPriorityAndNormalWeights()
    {
        Assert.Equal(3, QueueFairnessRotation.Advance(3, priorityPick: true, priorityWeight: 3, normalWeight: 1));
        Assert.Equal(-1, QueueFairnessRotation.Advance(-1, priorityPick: false, priorityWeight: 3, normalWeight: 1));
    }

    [Fact]
    public void ResolveEffectiveCounter_ResetsWhenPolicyVersionChangesAndClampsWhenMatching()
    {
        var policyV2 = new QueueOrderingPolicy(
            GraceMinutes: 15,
            ExpectedDurationMinutes: 15,
            SafetyBufferMinutes: 5,
            PriorityWeight: 3,
            NormalWeight: 1,
            WalkinPriorityRatioVersion: 2);

        Assert.Equal(0, QueueFairnessRotation.ResolveEffectiveCounter(storedCounter: 2, storedPolicyVersion: 1, policyV2));
        Assert.Equal(0, QueueFairnessRotation.ResolveEffectiveCounter(storedCounter: null, storedPolicyVersion: 2, policyV2));
        Assert.Equal(2, QueueFairnessRotation.ResolveEffectiveCounter(storedCounter: 2, storedPolicyVersion: 2, policyV2));
        Assert.Equal(3, QueueFairnessRotation.ResolveEffectiveCounter(storedCounter: 9, storedPolicyVersion: 2, policyV2));
        Assert.Equal(-1, QueueFairnessRotation.ResolveEffectiveCounter(storedCounter: -5, storedPolicyVersion: 2, policyV2));
    }
}
