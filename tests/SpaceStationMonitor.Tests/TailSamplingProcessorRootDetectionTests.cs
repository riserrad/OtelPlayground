using System.Diagnostics;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class TailSamplingProcessorRootDetectionTests : IDisposable
{
    private static readonly ActivitySource Source = new("TailSamplingProcessorRootDetectionTests");
    private readonly ActivityListener _listener;

    public TailSamplingProcessorRootDetectionTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "TailSamplingProcessorRootDetectionTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void LocalRoot_WithDefaultParentSpanId_TriggersRootSeen()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(
            graceWindow: TimeSpan.FromMilliseconds(200),
            inactivityTimeout: TimeSpan.FromSeconds(2),
            nowProvider: () => clock.Now);

        using var local = StartLocalRoot();
        Assert.Null(local.Parent);
        Assert.Equal(default, local.ParentSpanId);
        local.Stop();
        processor.OnEnd(local);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        Assert.NotEqual(TailSamplingDecision.Pending, processor.GetDecision(local.TraceId));
    }

    [Fact]
    public void RemoteParented_WithNonDefaultParentSpanId_DoesNotTriggerRootSeen()
    {
        var clock = new TestClock();
        var processor = new TailSamplingProcessor(
            graceWindow: TimeSpan.FromMilliseconds(200),
            inactivityTimeout: TimeSpan.FromSeconds(2),
            nowProvider: () => clock.Now);

        using var remote = StartRemoteParented();
        Assert.Null(remote.Parent);
        Assert.NotEqual(default, remote.ParentSpanId);
        remote.Stop();
        processor.OnEnd(remote);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        processor.FlushExpired();

        Assert.Equal(TailSamplingDecision.Pending, processor.GetDecision(remote.TraceId));
    }

    [Fact]
    public void RemoteParentedTrace_FlushesViaInactivityTimeout()
    {
        var clock = new TestClock();
        var inactivityTimeout = TimeSpan.FromSeconds(2);
        var processor = new TailSamplingProcessor(
            graceWindow: TimeSpan.FromMilliseconds(200),
            inactivityTimeout: inactivityTimeout,
            nowProvider: () => clock.Now);

        using var remote = StartRemoteParented();
        remote.Stop();
        processor.OnEnd(remote);

        clock.Advance(inactivityTimeout + TimeSpan.FromMilliseconds(50));
        processor.FlushExpired();

        Assert.NotEqual(TailSamplingDecision.Pending, processor.GetDecision(remote.TraceId));
    }

    [Fact]
    public void RemoteParentedTrace_DoesNotFlushBeforeInactivityTimeout()
    {
        var clock = new TestClock();
        var inactivityTimeout = TimeSpan.FromSeconds(2);
        var processor = new TailSamplingProcessor(
            graceWindow: TimeSpan.FromMilliseconds(200),
            inactivityTimeout: inactivityTimeout,
            nowProvider: () => clock.Now);

        using var remote = StartRemoteParented();
        remote.Stop();
        processor.OnEnd(remote);

        clock.Advance(TimeSpan.FromMilliseconds(inactivityTimeout.TotalMilliseconds / 2));
        processor.FlushExpired();

        Assert.Equal(TailSamplingDecision.Pending, processor.GetDecision(remote.TraceId));
    }

    private static Activity StartLocalRoot()
    {
        var a = Source.StartActivity("LocalRoot", ActivityKind.Internal, parentContext: default);
        Assert.NotNull(a);
        return a!;
    }

    private static Activity StartRemoteParented()
    {
        var ctx = new ActivityContext(
            traceId: ActivityTraceId.CreateRandom(),
            spanId: ActivitySpanId.CreateRandom(),
            traceFlags: ActivityTraceFlags.Recorded,
            traceState: null,
            isRemote: true);
        var a = Source.StartActivity("RemoteParented", ActivityKind.Internal, parentContext: ctx);
        Assert.NotNull(a);
        return a!;
    }

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc);
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);
    }
}
