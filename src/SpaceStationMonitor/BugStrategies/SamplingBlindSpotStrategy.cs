using OpenTelemetry.Trace;

namespace SpaceStationMonitor.BugStrategies;

// All gameplay-side hooks (OnRepair, retries, cycle counter, cascade reset,
// degradation redirect, repair delay) stay no-op. The visible effect comes
// from a sidecar in Program.cs that swaps the sampler's OverrideSampler when
// IsBugActive flips — see dev-design §0.5 (Path 2). Counters keep recording
// at full fidelity; only spans drop, which is the teaching beat.
public class SamplingBlindSpotStrategy : BugStrategyBase
{
    // Allocated once and reused — matches B2's pattern for HullThresholdSampler's
    // ratio-based sampler. Per-cycle allocation would churn small objects in the hot path.
    public static readonly Sampler HostileSampler = new TraceIdRatioBasedSampler(0.05);

    public SamplingBlindSpotStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "SamplingBlindSpot";
}
