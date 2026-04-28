using System.Diagnostics;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class TailSamplingProcessorTests : IDisposable
{
    private static readonly ActivitySource Source = new("TailSamplingProcessorTests");
    private readonly ActivityListener _listener;

    public TailSamplingProcessorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "TailSamplingProcessorTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void TraceWithErrorSpan_IsKept()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.SetStatus(ActivityStatusCode.Error);
        root.Stop();
        processor.OnEnd(root);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));
    }

    [Fact]
    public void TraceWithCascadeTag_IsKept()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.SetTag("cascade.triggered", true);
        root.Stop();
        processor.OnEnd(root);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));
    }

    [Fact]
    public void TraceWithFailedRepairTag_IsKept()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.SetTag("repair.healthy", false);
        root.Stop();
        processor.OnEnd(root);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));
    }

    [Fact]
    public void TraceWithoutTriggers_IsRatioSampled()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        var traceIds = new ActivityTraceId[1000];
        for (int i = 0; i < 1000; i++)
        {
            using var root = StartRoot();
            // No error / cascade / failed-repair tags, falls into the 25%-by-hash bucket.
            root.Stop();
            processor.OnEnd(root);
            traceIds[i] = root.TraceId;
        }

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        int kept = traceIds.Count(id => processor.GetDecision(id) == TailSamplingDecision.Kept);

        Assert.InRange(kept, 200, 300);
    }

    [Fact]
    public void BufferOverflow_ForcesOldestTraceDecision()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        // Fill the buffer to exactly the cap. RootSeen flips true on each OnEnd because
        // StartRoot produces parentless activities, but the test clock is not advanced, so
        // FlushExpiredLocked sees (now - RootEndTime) < graceWindow and every trace stays Pending.
        var pendingIds = new ActivityTraceId[TailSamplingProcessor.DefaultBufferCap];
        for (int i = 0; i < TailSamplingProcessor.DefaultBufferCap; i++)
        {
            using var act = StartRoot();
            processor.OnEnd(act);
            pendingIds[i] = act.TraceId;
        }

        // Sanity: every one of those is still Pending (no root-end recorded).
        Assert.Equal(TailSamplingDecision.Pending, processor.GetDecision(pendingIds[0]));

        // Pushing a new trace evicts the oldest and force-decides it.
        using var nthPlusOne = StartRoot();
        processor.OnEnd(nthPlusOne);

        Assert.NotEqual(TailSamplingDecision.Pending, processor.GetDecision(pendingIds[0]));
    }

    [Fact]
    public void OnShutdown_FlushesBufferedTraces()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.SetStatus(ActivityStatusCode.Error);
        root.Stop();
        processor.OnEnd(root);

        // Without flushing, the trace is still Pending because the test clock has not
        // advanced past the grace window.
        Assert.Equal(TailSamplingDecision.Pending, processor.GetDecision(root.TraceId));
        Assert.Equal(1, processor.BufferedTraceCount);

        // Shutdown forces a decision on every still-buffered trace.
        Assert.True(processor.Shutdown());

        Assert.Equal(0, processor.BufferedTraceCount);
        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));
    }

    [Fact]
    public void OnEnd_AfterDecision_DoesNotRebuffer()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.SetStatus(ActivityStatusCode.Error);
        root.Stop();
        processor.OnEnd(root);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();
        Assert.Equal(0, processor.BufferedTraceCount);
        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));

        // A child span on the same TraceId arrives after the keep/drop decision lands.
        using var lateChild = Source.StartActivity(
            "LateChild", ActivityKind.Internal,
            parentContext: new ActivityContext(root.TraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded));
        Assert.NotNull(lateChild);
        lateChild!.Stop();
        processor.OnEnd(lateChild);

        // The processor must not seed a fresh buffer entry for this TraceId, otherwise
        // a long-lived process would leak memory and a Kept trace would ship twice.
        Assert.Equal(0, processor.BufferedTraceCount);
        Assert.Equal(TailSamplingDecision.Kept, processor.GetDecision(root.TraceId));
    }

    [Fact]
    public void DeterministicKeepRule_StableAcrossInstances()
    {
        var traceIds = Enumerable.Range(0, 200).Select(_ => ActivityTraceId.CreateRandom()).ToArray();

        var decisionsA = traceIds.Select(id =>
            TailSamplingProcessor.ShouldKeep(Array.Empty<Activity>(), id)).ToArray();
        var decisionsB = traceIds.Select(id =>
            TailSamplingProcessor.ShouldKeep(Array.Empty<Activity>(), id)).ToArray();

        Assert.Equal(decisionsA, decisionsB);

        // The rule must also produce a non-trivial split. If every decision was the same
        // value, an unstable hash could pass the equality check by accident.
        Assert.Contains(true, decisionsA);
        Assert.Contains(false, decisionsA);
    }

    [Fact]
    public void RootSpanEndPlusGrace_TriggersFlush()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(graceWindow: TimeSpan.FromMilliseconds(200), nowProvider: () => clock.Now);

        using var root = StartRoot();
        root.Stop();
        processor.OnEnd(root);

        // Before grace window elapses, decision is still pending.
        clock.Advance(TimeSpan.FromMilliseconds(150));
        processor.FlushExpired();
        Assert.Equal(TailSamplingDecision.Pending, processor.GetDecision(root.TraceId));

        // After grace window elapses, decision lands.
        clock.Advance(TimeSpan.FromMilliseconds(100));
        processor.FlushExpired();
        Assert.NotEqual(TailSamplingDecision.Pending, processor.GetDecision(root.TraceId));
    }

    private static Activity StartRoot()
    {
        var a = Source.StartActivity("Root", ActivityKind.Internal, parentContext: default);
        Assert.NotNull(a);
        return a!;
    }

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);
    }
}
