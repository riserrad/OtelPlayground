using OpenTelemetry.Trace;

namespace SpaceStationMonitor.Sampling;

public enum SamplingRegime
{
    Calm,
    Storm,
}

/// <summary>
/// Hull-driven sampler with hysteresis.
///   Storm  (RecordAndSample): hull ≤ 70.0
///   Calm   (TraceIdRatioBased(0.10)): hull > 75.0
///   Dead-band (70 &lt; hull ≤ 75): keep current regime — avoids per-cycle flicker.
/// </summary>
public sealed class HullThresholdSampler : Sampler
{
    public const double StormThreshold = 70.0;
    public const double CalmThreshold = 75.0;

    private readonly Func<double> _hullProvider;
    private readonly Sampler _ratioBased;
    private SamplingRegime _currentRegime;

    public HullThresholdSampler(Func<double> hullProvider)
    {
        _hullProvider = hullProvider;
        // Constructed once and reused — never re-allocated per ShouldSample call.
        _ratioBased = new TraceIdRatioBasedSampler(0.10);
        Description = "HullThresholdSampler{calm=ratio(0.10),storm=record,hyst=70/75}";

        // Seed initial regime from the starting hull so a freshly-built sampler
        // matches the visible state (e.g., dropped into a fight already at hull=60).
        _currentRegime = hullProvider() <= StormThreshold
            ? SamplingRegime.Storm
            : SamplingRegime.Calm;
    }

    /// <summary>
    /// When non-null, ShouldSample delegates to this sampler and ignores hull state.
    /// Set by D2 (SamplingBlindSpot strategy) to pin a hostile fixed-rate sampler.
    /// </summary>
    public Sampler? OverrideSampler { get; set; }

    public SamplingRegime CurrentRegime => _currentRegime;

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (OverrideSampler is { } overrideSampler)
            return overrideSampler.ShouldSample(in samplingParameters);

        UpdateRegime();

        return _currentRegime == SamplingRegime.Storm
            ? new SamplingResult(SamplingDecision.RecordAndSample)
            : _ratioBased.ShouldSample(in samplingParameters);
    }

    private void UpdateRegime()
    {
        double hull = _hullProvider();

        // Hysteresis: only flip when crossing the OPPOSITE threshold for the current regime.
        // Calm → Storm: hull crosses down through 70.
        // Storm → Calm: hull crosses up through 75.
        // Dead-band (70 < hull ≤ 75): no transition.
        if (_currentRegime == SamplingRegime.Calm)
        {
            if (hull <= StormThreshold)
                _currentRegime = SamplingRegime.Storm;
        }
        else
        {
            if (hull > CalmThreshold)
                _currentRegime = SamplingRegime.Calm;
        }
    }
}
