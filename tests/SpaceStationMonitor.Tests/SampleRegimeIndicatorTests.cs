using SpaceStationMonitor.Sampling;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class SampleRegimeIndicatorTests
{
    [Fact]
    public void HullThreshold_NoSamplerOrCalm_ShowsIdleBadge_DarkCyan()
    {
        var indicator = new SampleRegimeIndicator(SamplerProfileKind.HullThreshold);
        var (text, color) = indicator.CurrentBadge;
        Assert.Equal("◌ idle", text);
        Assert.Equal(ConsoleColor.DarkCyan, color);
    }

    [Fact]
    public void HullThreshold_StormRegime_ShowsRecBadge_Red()
    {
        // A low-hull provider drives the inner sampler into Storm regime; the indicator reads
        // CurrentRegime directly, so a quick query is enough to land in Storm.
        var hull = new HullThresholdSampler(() => 30.0);
        var indicator = new SampleRegimeIndicator(SamplerProfileKind.HullThreshold, hull);

        var (text, color) = indicator.CurrentBadge;

        Assert.Equal("◉ rec", text);
        Assert.Equal(ConsoleColor.Red, color);
    }

    [Fact]
    public void AlwaysOn_ShowsAllBadge_Cyan()
    {
        var indicator = new SampleRegimeIndicator(SamplerProfileKind.AlwaysOn);
        var (text, color) = indicator.CurrentBadge;
        Assert.Equal("◉ all", text);
        Assert.Equal(ConsoleColor.Cyan, color);
    }

    [Fact]
    public void Tail_ShowsTailBadge_Yellow()
    {
        var indicator = new SampleRegimeIndicator(SamplerProfileKind.Tail);
        var (text, color) = indicator.CurrentBadge;
        Assert.Equal("◈ tail", text);
        Assert.Equal(ConsoleColor.Yellow, color);
    }

    [Fact]
    public void Rules_ShowsRulesBadge_Magenta()
    {
        var indicator = new SampleRegimeIndicator(SamplerProfileKind.Rules);
        var (text, color) = indicator.CurrentBadge;
        Assert.Equal("◆ rules", text);
        Assert.Equal(ConsoleColor.Magenta, color);
    }

}
