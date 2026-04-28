using SpaceStationMonitor;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class EventEngineTests
{
    [Fact]
    public void TryGenerateEvent_MultiplierZero_NeverEmits()
    {
        var engine = new EventEngine(eventChanceMultiplier: 0.0);

        for (int i = 0; i < 100; i++)
            Assert.Null(engine.TryGenerateEvent(isBugActive: false));
    }

    [Fact]
    public void TryGenerateEvent_MultiplierLarge_AlwaysEmits()
    {
        var engine = new EventEngine(eventChanceMultiplier: 100.0);

        for (int i = 0; i < 100; i++)
            Assert.NotNull(engine.TryGenerateEvent(isBugActive: false));
    }

    [Fact]
    public void TryGenerateEvent_DefaultMultiplier_RunsWithoutThrowing()
    {
        var engineDefault = new EventEngine();
        var engineExplicit = new EventEngine(eventChanceMultiplier: 1.0);

        for (int i = 0; i < 100; i++)
        {
            engineDefault.TryGenerateEvent(isBugActive: false);
            engineExplicit.TryGenerateEvent(isBugActive: true);
        }
    }
}
