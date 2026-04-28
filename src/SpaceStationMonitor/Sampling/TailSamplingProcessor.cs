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
        if (graceWindow is { } gw && gw <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(graceWindow));
        if (inactivityTimeout is { } it && it <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inactivityTimeout));
        _next = next;
        _bufferCap = bufferCap;
        _graceWindow = graceWindow ?? DefaultGraceWindow;
        _inactivityTimeout = inactivityTimeout ?? DefaultInactivityTimeout;
        _now = nowProvider ?? (() => DateTime.UtcNow);
    }

    public override void OnEnd(Activity data)
    {
        // Forwarding to the downstream processor must happen outside _lock: _next is
        // external code and may be slow or re-entrant, so holding the lock across the
        // call increases contention and risks deadlock. Every forwarding site in this
        // class follows the same shape: accumulate the decision under the lock, exit,
        // then forward.
        bool forwardLateSpan = false;
        List<Activity>? toForward = null;

        lock (_lock)
        {
            FlushExpiredLocked(ref toForward);

            var traceId = data.TraceId;

            // A late span (one arriving after the trace's keep/drop has been decided)
            // must not seed a fresh buffer entry under the same TraceId. Doing so
            // would leak memory and could ship the trace twice if it had been Kept.
            if (_decisions.TryGetValue(traceId, out var prior))
            {
                forwardLateSpan = prior == TailSamplingDecision.Kept && _next is not null;
            }
            else
            {
                if (!_buffers.TryGetValue(traceId, out var buffer))
                {
                    if (_buffers.Count >= _bufferCap)
                    {
                        var oldestNode = _insertionOrder.First;
                        if (oldestNode is not null)
                        {
                            _insertionOrder.RemoveFirst();
                            DecideLocked(oldestNode.Value, ref toForward);
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

        ForwardOutsideLock(toForward);

        if (forwardLateSpan)
        {
            _next!.OnEnd(data);
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
        => FlushAllBuffered(timeoutMilliseconds, isShutdown: false);

    /// <inheritdoc />
    /// <remarks>
    /// Same drain gesture as <see cref="OnForceFlush"/>, but propagates a true
    /// shutdown to the downstream processor so its own teardown logic (releasing
    /// network handles, finalizing background workers) runs. Calling
    /// <see cref="BaseProcessor{T}.ForceFlush(int)"/> here would leave that cleanup
    /// undone. The base class only invokes this once, so per-trace decisions are
    /// recorded normally in the decision cache.
    /// </remarks>
    protected override bool OnShutdown(int timeoutMilliseconds)
        => FlushAllBuffered(timeoutMilliseconds, isShutdown: true);

    private bool FlushAllBuffered(int timeoutMilliseconds, bool isShutdown)
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
            return isShutdown ? _next.Shutdown(remaining) : _next.ForceFlush(remaining);
        }

        return isShutdown ? _next.Shutdown() : _next.ForceFlush();
    }

    /// <summary>
    /// Last-recorded decision for the given trace, or <see cref="TailSamplingDecision.Pending"/>
    /// if the trace is still buffered or unseen.
    /// </summary>
    public TailSamplingDecision GetDecision(ActivityTraceId traceId)
    {
        List<Activity>? toForward = null;
        TailSamplingDecision result;
        lock (_lock)
        {
            FlushExpiredLocked(ref toForward);
            result = _decisions.TryGetValue(traceId, out var d) ? d : TailSamplingDecision.Pending;
        }
        ForwardOutsideLock(toForward);
        return result;
    }

    /// <summary>
    /// Decides any traces whose root span ended more than the grace window ago. Driven naturally
    /// by inbound <see cref="OnEnd"/> calls in production; tests call this directly to avoid
    /// real-time waits.
    /// </summary>
    public void FlushExpired()
    {
        List<Activity>? toForward = null;
        lock (_lock) FlushExpiredLocked(ref toForward);
        ForwardOutsideLock(toForward);
    }

    private void FlushExpiredLocked(ref List<Activity>? toForward)
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
            // inactivity timeout. Guarded on !RootSeen to make the contract explicit:
            // any trace whose local root has been observed belongs to the grace path.
            if (!buf.RootSeen && (now - buf.LastActivityTime) >= _inactivityTimeout)
                (toDecide ??= new()).Add(id);
        }
        if (toDecide is null) return;
        foreach (var id in toDecide)
        {
            _insertionOrder.Remove(id);
            DecideLocked(id, ref toForward);
        }
    }

    private void DecideLocked(ActivityTraceId traceId, ref List<Activity>? toForward)
    {
        if (!_buffers.Remove(traceId, out var buffer)) return;

        bool keep = ShouldKeep(buffer.Activities, traceId);
        _decisions[traceId] = keep ? TailSamplingDecision.Kept : TailSamplingDecision.Dropped;

        if (keep)
        {
            (toForward ??= new()).AddRange(buffer.Activities);
        }
    }

    private void ForwardOutsideLock(List<Activity>? toForward)
    {
        if (toForward is null || _next is null) return;
        foreach (var act in toForward)
            _next.OnEnd(act);
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
        public DateTime LastActivityTime;
    }
}
