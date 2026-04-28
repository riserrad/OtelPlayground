using System.Diagnostics;

namespace SpaceStationMonitor.BugStrategies;

/// <summary>
/// Injects a synthetic remote-parent <see cref="ActivityContext"/> on every Nth cycle once
/// active. The cycle activity is then started as if propagated from an upstream service:
/// non-default <c>ParentSpanId</c>, <c>HasRemoteParent == true</c>, sampled-in via
/// <see cref="ActivityTraceFlags.Recorded"/>.
///
/// The synthetic <see cref="ActivityTraceId"/> is fixed at construction and reused across
/// every injection for the lifetime of the strategy instance. Each injected cycle gets a
/// fresh <see cref="ActivitySpanId"/> as the synthetic upstream parent span. This mirrors a
/// real downstream service receiving the same W3C <c>traceparent</c> trace identity on
/// every call from a single upstream sidecar, with each call carrying its own parent span.
/// </summary>
public sealed class OrphanSpanStrategy : BugStrategyBase
{
    private const int InjectEveryNthCycle = 3;

    private readonly Func<int> _cycleProvider;
    private readonly ActivityTraceId _syntheticTraceId;

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
        _syntheticTraceId = ActivityTraceId.CreateRandom();
    }

    public override ActivityContext? OverrideStationCycleParent()
    {
        if (!IsBugActive) return null;

        var cycle = _cycleProvider();
        if (cycle <= 0 || cycle % InjectEveryNthCycle != 0) return null;

        InjectedCount++;
        return new ActivityContext(
            traceId: _syntheticTraceId,
            spanId: ActivitySpanId.CreateRandom(),
            traceFlags: ActivityTraceFlags.Recorded,
            traceState: null,
            isRemote: true);
    }
}
