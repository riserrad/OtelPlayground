using System.Diagnostics;
using OpenTelemetry.Trace;
using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class SamplerProfileFactoryTests
{
    [Fact]
    public void HullThreshold_BuildsParentBased_NoTailProcessor_ExposesHullSampler_RecordsInStormRegime()
    {
        var profile = SamplerProfileFactory.Build(SamplerProfileKind.HullThreshold, () => 30.0);

        Assert.IsType<ParentBasedSampler>(profile.HeadSampler);
        Assert.Null(profile.TailProcessor);
        Assert.NotNull(profile.HullSampler);

        // Behavioral check: low hull drives the inner sampler into Storm regime, which records.
        var result = profile.HeadSampler.ShouldSample(MakeParams("StationCycle"));
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void AlwaysOn_BuildsAlwaysOnHead_NoTailProcessor_NoHullSampler_RecordsEveryProbe()
    {
        var profile = SamplerProfileFactory.Build(SamplerProfileKind.AlwaysOn, () => 100.0);

        Assert.IsType<AlwaysOnSampler>(profile.HeadSampler);
        Assert.Null(profile.TailProcessor);
        Assert.Null(profile.HullSampler);

        var result = profile.HeadSampler.ShouldSample(MakeParams("AnyName"));
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void Tail_BuildsAlwaysOnHead_WithTailSamplingProcessor_NoHullSampler_HeadRecordsEveryProbe()
    {
        var profile = SamplerProfileFactory.Build(SamplerProfileKind.Tail, () => 100.0);

        Assert.IsType<AlwaysOnSampler>(profile.HeadSampler);
        Assert.IsType<TailSamplingProcessor>(profile.TailProcessor);
        Assert.Null(profile.HullSampler);

        var result = profile.HeadSampler.ShouldSample(MakeParams("AnyName"));
        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void Rules_BuildsParentBasedRulesSampler_NoTailProcessor_NoHullSampler_RoutesByName()
    {
        var profile = SamplerProfileFactory.Build(SamplerProfileKind.Rules, () => 100.0);

        Assert.IsType<ParentBasedSampler>(profile.HeadSampler);
        Assert.Null(profile.TailProcessor);
        Assert.Null(profile.HullSampler);

        // Behavioral check: RepairAction must be RecordAndSample on any TraceId.
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var parent = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded, isRemote: false);
        var p = new SamplingParameters(parent, traceId, "RepairAction", ActivityKind.Internal, null, null);

        var result = profile.HeadSampler.ShouldSample(p);

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Fact]
    public void UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SamplerProfileFactory.Build((SamplerProfileKind)999, () => 100.0));
    }

    private static SamplingParameters MakeParams(string name)
        => new SamplingParameters(
            parentContext: default,
            traceId: ActivityTraceId.CreateRandom(),
            name: name,
            kind: ActivityKind.Internal,
            tags: null,
            links: null);
}
