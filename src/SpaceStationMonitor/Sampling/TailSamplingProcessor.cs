using System.Buffers.Binary;
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
    private const double SampleRatio = 0.25;

    private readonly BaseProcessor<Activity>? _next;
    private readonly int _bufferCap;
    private readonly TimeSpan _graceWindow;
    private readonly Func<DateTime> _now;

    private readonly object _lock = new();
    private readonly Dictionary<ActivityTraceId, TraceBuffer> _buffers = new();
    private readonly LinkedList<ActivityTraceId> _insertionOrder = new();
    private readonly Dictionary<ActivityTraceId, TailSamplingDecision> _decisions = new();

    public TailSamplingProcessor(
        BaseProcessor<Activity>? next = null,
        int bufferCap = DefaultBufferCap,
        TimeSpan? graceWindow = null,
        Func<DateTime>? nowProvider = null)
    {
        if (bufferCap <= 0) throw new ArgumentOutOfRangeException(nameof(bufferCap));
        _next = next;
        _bufferCap = bufferCap;
        _graceWindow = graceWindow ?? DefaultGraceWindow;
        _now = nowProvider ?? (() => DateTime.UtcNow);
    }

    public override void OnEnd(Activity data)
    {
        lock (_lock)
        {
            FlushExpiredLocked();

            var traceId = data.TraceId;

            // A late span (one arriving after the trace's keep/drop has been decided)
            // must not seed a fresh buffer entry under the same TraceId. Doing so
            // would leak memory and could ship the trace twice if it had been Kept.
            if (_decisions.TryGetValue(traceId, out var prior))
            {
                if (prior == TailSamplingDecision.Kept && _next is not null)
                    _next.OnEnd(data);
                return;
            }

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
            if (data.Parent is null)
            {
                buffer.RootSeen = true;
                buffer.RootEndTime = _now();
            }
        }
    }

    /// <summary>
    /// Number of traces currently buffered awaiting a decision. Useful for tests and
    /// for confirming buffer drain on shutdown / force-flush.
    /// </summary>
    public int BufferedTraceCount
    {
        get { lock (_lock) return _buffers.Count; }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decides every still-buffered trace by applying the standard keep/drop rules,
    /// then asks the downstream processor (if any) to flush. Without this override,
    /// buffered traces would be dropped silently when the SDK tears down.
    /// </remarks>
    protected override bool OnForceFlush(int timeoutMilliseconds)
        => FlushAllBuffered(timeoutMilliseconds);

    /// <inheritdoc />
    /// <remarks>
    /// Same gesture as <see cref="OnForceFlush"/>: decide every still-buffered trace
    /// and propagate the shutdown to the downstream processor. The base class only
    /// invokes this once, so per-trace decisions are recorded normally in the
    /// decision cache.
    /// </remarks>
    protected override bool OnShutdown(int timeoutMilliseconds)
        => FlushAllBuffered(timeoutMilliseconds);

    private bool FlushAllBuffered(int timeoutMilliseconds)
    {
        DateTime? deadline = timeoutMilliseconds == Timeout.Infinite
            ? null
            : _now().AddMilliseconds(timeoutMilliseconds);

        List<Activity>? toForward = null;

        lock (_lock)
        {
            // Snapshot keys first so we can mutate _buffers inside the loop.
            var traceIds = new ActivityTraceId[_buffers.Count];
            int idx = 0;
            foreach (var key in _buffers.Keys) traceIds[idx++] = key;

            foreach (var traceId in traceIds)
            {
                if (deadline.HasValue && _now() >= deadline.Value)
                    return false;

                if (!_buffers.Remove(traceId, out var buffer)) continue;
                _insertionOrder.Remove(traceId);

                bool keep = ShouldKeep(buffer.Activities, traceId);
                _decisions[traceId] = keep ? TailSamplingDecision.Kept : TailSamplingDecision.Dropped;

                if (keep)
                {
                    (toForward ??= new()).AddRange(buffer.Activities);
                }
            }
        }

        if (toForward is not null && _next is not null)
        {
            foreach (var act in toForward)
            {
                if (deadline.HasValue && _now() >= deadline.Value) return false;
                _next.OnEnd(act);
            }
        }

        if (_next is null) return true;

        if (deadline.HasValue)
        {
            int remaining = (int)Math.Max(0, (deadline.Value - _now()).TotalMilliseconds);
            return _next.ForceFlush(remaining);
        }

        return _next.ForceFlush();
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
            if (buf.RootSeen && (now - buf.RootEndTime) >= _graceWindow)
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

        // Default fallback: deterministic 25% sample using the raw TraceId bytes.
        // .NET's Object.GetHashCode is not guaranteed stable across processes or
        // framework versions, so a hash-based sample built on it would land
        // differently for the same TraceId in a multi-process pipeline.
        Span<byte> bytes = stackalloc byte[16];
        traceId.CopyTo(bytes);
        uint stable = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return (stable & 0xFFFF) < 0xFFFF * SampleRatio;
    }

    private sealed class TraceBuffer
    {
        public readonly List<Activity> Activities = new();
        public bool RootSeen;
        public DateTime RootEndTime;
    }
}
