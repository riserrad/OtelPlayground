namespace SpaceStationMonitor.Sampling;

/// <summary>
/// HUD adapter that maps the active <see cref="SamplerProfileKind"/> (and, for the
/// HullThreshold profile, the live <see cref="HullThresholdSampler"/> regime) to a badge
/// glyph + color. Decouples <see cref="GameDisplay"/> from the sampler taxonomy.
/// </summary>
public sealed class SampleRegimeIndicator
{
    private readonly SamplerProfileKind _profile;
    private readonly HullThresholdSampler? _hullSampler;

    public SampleRegimeIndicator(SamplerProfileKind profile, HullThresholdSampler? hullSampler = null)
    {
        _profile = profile;
        _hullSampler = hullSampler;
    }

    public SamplerProfileKind Profile => _profile;

    public (string BadgeText, ConsoleColor BadgeColor) CurrentBadge => _profile switch
    {
        SamplerProfileKind.HullThreshold => _hullSampler?.CurrentRegime == SamplingRegime.Storm
            ? ("◉ rec", ConsoleColor.Red)
            : ("◌ idle", ConsoleColor.DarkCyan),
        SamplerProfileKind.AlwaysOn => ("◉ all", ConsoleColor.Cyan),
        SamplerProfileKind.Tail => ("◈ tail", ConsoleColor.Yellow),
        SamplerProfileKind.Rules => ("◆ rules", ConsoleColor.Magenta),
        _ => ("?", ConsoleColor.Gray),
    };
}
