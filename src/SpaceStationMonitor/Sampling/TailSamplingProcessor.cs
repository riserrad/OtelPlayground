using System.Diagnostics;
using OpenTelemetry;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// Decision recorded by the tail sampler for a given trace.
/// </summary>
public enum TailSamplingDecision
{
    Pending,
    Kept,
    Dropped,
}

/// <summary>
/// In-process tail-sampling processor. Buffers activities by <see cref="Activity.TraceId"/>,
/// decides keep/drop on root-span end + grace window. Errors, cascade-triggered traces, and
/// failed-repair traces are kept; everything else falls into a 25% TraceId-hash sample.
/// Real production tail sampling lives in the OpenTelemetry collector; this is a teaching
/// simulation suitable for a single-process console app. See docs/bug-catalog-debugging.md.
/// </summary>
public sealed class TailSamplingProcessor : BaseProcessor<Activity>
{
    public const int DefaultBufferCap = 1000;
    public static readonly TimeSpan DefaultGraceWindow = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromSeconds(2);
    private const double SampleRatio = 0.25;

    private readonly BaseProcessor<Activity>? _next;
    private readonly int _bufferCap;
    private readonly TimeSpan _graceWindow;
    private readonly TimeSpan _inactivityTimeout;
    private readonly Func<DateTime> _now;

    private readonly object _lock = new();
    private readonly Dictionary<ActivityTraceId, TraceBuffer> _buffers = new();
    private readonly LinkedList<ActivityTraceId> _insertionOrder = new();
    private readonly Dictionary<ActivityTraceId, TailSamplingDecision> _decisions = new();

    public TailSamplingProcessor(
        BaseProcessor<Activity>? next = null,
        int bufferCap = DefaultBufferCap,
        TimeSpan? graceWindow = null,
        TimeSpan? inactivityTimeout = null,
        Func<DateTime>? nowProvider = null)
    {
        if (bufferCap <= 0) throw new ArgumentOutOfRangeException(nameof(bufferCap));
        _next = next;
        _bufferCap = bufferCap;
        _graceWindow = graceWindow ?? DefaultGraceWindow;
        _inactivityTimeout = inactivityTimeout ?? DefaultInactivityTimeout;
        _now = nowProvider ?? (() => DateTime.UtcNow);
    }

    public override void OnEnd(Activity data)
    {
        lock (_lock)
        {
            FlushExpiredLocked();

            var traceId = data.TraceId;
            if (!_buffers.TryGetValue(traceId, out var buffer))
            {
                if (_buffers.Count >= _bufferCap)
                {
                    var oldestNode = _insertionOrder.First;
                    if (oldestNode is not null)
                    {
                        _insertionOrder.RemoveFirst();
                        DecideAndForwardLocked(oldestNode.Value);
                    }
                }
                buffer = new TraceBuffer();
                _buffers[traceId] = buffer;
                _insertionOrder.AddLast(traceId);
            }

            buffer.Activities.Add(data);
            buffer.LastActivityTime = _now();
            // True local root: no in-process parent AND no propagated remote parent.
            // A non-default ParentSpanId (set via W3C traceparent on a cross-process child)
            // means this Activity has a remote parent, so the trace's true root lives
            // elsewhere. Treating it as the local root would flush the buffer prematurely
            // while later in-process children for the same trace are still arriving.
            if (data.Parent is null && data.ParentSpanId == default)
            {
                buffer.RootSeen = true;
                buffer.RootEndTime = _now();
            }
        }
    }

    /// <summary>
    /// Last-recorded decision for the given trace, or <see cref="TailSamplingDecision.Pending"/>
    /// if the trace is still buffered or unseen.
    /// </summary>
    public TailSamplingDecision GetDecision(ActivityTraceId traceId)
    {
        lock (_lock)
        {
            FlushExpiredLocked();
            return _decisions.TryGetValue(traceId, out var d) ? d : TailSamplingDecision.Pending;
        }
    }

    /// <summary>
    /// Decides any traces whose root span ended more than the grace window ago. Driven naturally
    /// by inbound <see cref="OnEnd"/> calls in production; tests call this directly to avoid
    /// real-time waits.
    /// </summary>
    public void FlushExpired()
    {
        lock (_lock) FlushExpiredLocked();
    }

    private void FlushExpiredLocked()
    {
        var now = _now();
        List<ActivityTraceId>? toDecide = null;
        foreach (var (id, buf) in _buffers)
        {
            // Primary path: a local root has ended and the grace window for trailing
            // in-process children has elapsed.
            if (buf.RootSeen && (now - buf.RootEndTime) >= _graceWindow)
            {
                (toDecide ??= new()).Add(id);
                continue;
            }
            // Fallback path: the trace was remote-parented, so this process never
            // observes a local root; flush once activity has been quiet for the
            // inactivity timeout.
            if ((now - buf.LastActivityTime) >= _inactivityTimeout)
                (toDecide ??= new()).Add(id);
        }
        if (toDecide is null) return;
        foreach (var id in toDecide)
        {
            _insertionOrder.Remove(id);
            DecideAndForwardLocked(id);
        }
    }

    private void DecideAndForwardLocked(ActivityTraceId traceId)
    {
        if (!_buffers.Remove(traceId, out var buffer)) return;

        bool keep = ShouldKeep(buffer.Activities, traceId);
        _decisions[traceId] = keep ? TailSamplingDecision.Kept : TailSamplingDecision.Dropped;

        if (keep && _next is not null)
        {
            foreach (var act in buffer.Activities)
                _next.OnEnd(act);
        }
    }

    internal static bool ShouldKeep(IEnumerable<Activity> activities, ActivityTraceId traceId)
    {
        bool sawError = false, sawCascade = false, sawFailedRepair = false;

        foreach (var a in activities)
        {
            if (a.Status == ActivityStatusCode.Error) sawError = true;
            foreach (var tag in a.TagObjects)
            {
                if (tag.Key == "cascade.triggered" && tag.Value is true) sawCascade = true;
                if (tag.Key == "repair.healthy" && tag.Value is false) sawFailedRepair = true;
            }
        }

        if (sawError || sawCascade || sawFailedRepair) return true;

        // Default fallback: deterministic 25% by TraceId hash.
        return (traceId.GetHashCode() & 0xFFFF) < 0xFFFF * SampleRatio;
    }

    private sealed class TraceBuffer
    {
        public readonly List<Activity> Activities = new();
        public bool RootSeen;
        public DateTime RootEndTime;
        public DateTime LastActivityTime;
    }
}
