using OpenTelemetry.Trace;

namespace SpaceStationMonitor.BugStrategies;

// IBugStrategy hooks (OnRepair, retries, cycle counter, cascade reset,
// degradation redirect, repair delay) all stay no-op for this strategy.
// The teaching-beat effect lives at the sampler boundary: while the bug is
// active, the per-cycle override toggle redirects sampling decisions to
// HostileSampler, dropping ~95% of spans while counters keep recording at
// full fidelity. Activation policy is the standard BugStrategyBase delay.
public class SamplingBlindSpotStrategy : BugStrategyBase
{
    // Reused across activations: the rate is fixed, so a fresh allocation per
    // toggle would only churn small objects without changing behavior.
    public static readonly Sampler HostileSampler = new TraceIdRatioBasedSampler(0.05);

    public SamplingBlindSpotStrategy(string bugTargetSubsystem, TimeSpan? activationDelay = null)
        : base(bugTargetSubsystem, activationDelay) { }

    public override string Name => "SamplingBlindSpot";
}
