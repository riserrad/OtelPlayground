using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// The (head sampler, optional tail processor, optional inner hull-threshold sampler) bundle
/// produced by <see cref="SamplerProfileFactory"/>. The third field exposes the inner
/// <see cref="HullThresholdSampler"/> for the HullThreshold profile so the HUD's
/// <see cref="SampleRegimeIndicator"/> can read regime state from the same instance the
/// trace pipeline samples on.
/// </summary>
public readonly record struct SamplerProfile(
    Sampler HeadSampler,
    BaseProcessor<Activity>? TailProcessor,
    HullThresholdSampler? HullSampler);

/// <summary>
/// Builds the sampler / processor pair for a given <see cref="SamplerProfileKind"/>. The factory
/// is mode-agnostic; the caller resolves <see cref="GameMode"/> to a default profile and supplies
/// the export pipeline (<paramref name="nextFactory"/>) that the tail processor's <c>_next</c>
/// should forward kept activities through. <paramref name="nextFactory"/> is invoked only for
/// the Tail profile and may be <c>null</c> in unit tests that exercise tail keep/drop logic in
/// isolation.
/// </summary>
public static class SamplerProfileFactory
{
    public static SamplerProfile Build(
        SamplerProfileKind profile,
        Func<double> hullProvider,
        Func<BaseProcessor<Activity>>? nextFactory = null)
    {
        return profile switch
        {
            SamplerProfileKind.HullThreshold => BuildHull(hullProvider),
            SamplerProfileKind.AlwaysOn => new SamplerProfile(new AlwaysOnSampler(), null, null),
            SamplerProfileKind.Tail => new SamplerProfile(
                new AlwaysOnSampler(),
                new TailSamplingProcessor(next: nextFactory?.Invoke()),
                null),
            SamplerProfileKind.Rules => new SamplerProfile(new ParentBasedSampler(new RulesBasedSampler()), null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown sampler profile kind"),
        };
    }

    private static SamplerProfile BuildHull(Func<double> hullProvider)
    {
        var hull = new HullThresholdSampler(hullProvider);
        return new SamplerProfile(new ParentBasedSampler(hull), null, hull);
    }
}
