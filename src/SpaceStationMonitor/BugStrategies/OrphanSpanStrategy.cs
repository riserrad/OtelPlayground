using System.Diagnostics;

namespace SpaceStationMonitor.BugStrategies;

/// <summary>
/// Injects a synthetic remote-parent <see cref="ActivityContext"/> on every Nth cycle once
/// active. The cycle activity is then started as if propagated from an upstream service:
/// non-default <c>ParentSpanId</c>, <c>HasRemoteParent == true</c>, sampled-in via
/// <see cref="ActivityTraceFlags.Recorded"/>.
///
/// TraceId design choice (Path A): the synthetic context inherits the ambient trace's
/// <c>TraceId</c> when one is current, otherwise a fresh random TraceId is generated for the
/// injected cycle. This mirrors the production model where a downstream service receives a
/// W3C traceparent header carrying the upstream's TraceId and starts its own root activity
/// under that trace.
/// </summary>
public sealed class OrphanSpanStrategy : BugStrategyBase
{
    private const int InjectEveryNthCycle = 3;

    private readonly Func<int> _cycleProvider;

    public override string Name => "OrphanSpan";

    /// <summary>Number of cycles where a synthetic remote-parent context was injected.</summary>
    public int InjectedCount { get; private set; }

    public OrphanSpanStrategy(
        string bugTarget,
        TimeSpan? activationDelay = null,
        Func<int>? cycleProvider = null)
        : base(bugTarget, activationDelay)
    {
        _cycleProvider = cycleProvider ?? (() => 0);
    }

    public override ActivityContext? OverrideStationCycleParent()
    {
        if (!IsBugActive) return null;

        var cycle = _cycleProvider();
        if (cycle <= 0 || cycle % InjectEveryNthCycle != 0) return null;

        var traceId = Activity.Current?.TraceId ?? ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        InjectedCount++;
        return new ActivityContext(
            traceId: traceId,
            spanId: spanId,
            traceFlags: ActivityTraceFlags.Recorded,
            traceState: null,
            isRemote: true);
    }
}
