using OpenTelemetry.Trace;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// Per-span-name head sampler. RepairAction / StationEvent / CascadeCheck are always recorded;
/// SubsystemTick falls into a 10% TraceId-hash sample; everything else falls into a 25% sample.
/// Used by the Rules sampler profile (<c>SAMPLER_PROFILE=rules</c>).
/// </summary>
public sealed class RulesBasedSampler : Sampler
{
    private readonly Sampler _tickSampler = new TraceIdRatioBasedSampler(0.10);
    private readonly Sampler _defaultSampler = new TraceIdRatioBasedSampler(0.25);

    public RulesBasedSampler()
    {
        Description = "RulesBasedSampler{repair=record,event=record,cascade=record,tick=ratio(0.10),default=ratio(0.25)}";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        return samplingParameters.Name switch
        {
            "RepairAction" or "StationEvent" or "CascadeCheck"
                => new SamplingResult(SamplingDecision.RecordAndSample),
            "SubsystemTick" => _tickSampler.ShouldSample(in samplingParameters),
            _ => _defaultSampler.ShouldSample(in samplingParameters),
        };
    }
}
