using SpaceStationMonitor;
using Xunit;

namespace SpaceStationMonitor.Tests;

public class ActiveRepairsTests
{
    private static InFlightRepair MakeEntry(Subsystem sub, int cycles)
        => new(sub, Requested: 20, CyclesRemaining: cycles, RepairAction: null);

    private static Subsystem NewSub(string name = "Oxygen") => new(name, 1.0);

    [Fact]
    public void TryStart_BelowCapacity_Succeeds()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        var s1 = NewSub("Oxygen");
        var s2 = NewSub("Power");

        Assert.True(ar.TryStart(MakeEntry(s1, 1)));
        Assert.True(ar.TryStart(MakeEntry(s2, 2)));
        Assert.Equal(2, ar.InFlightCount);
        Assert.Equal(2, ar.AvailableSlots);
    }

    [Fact]
    public void TryStart_AtCapacity_ReturnsFalse()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 2);
        Assert.True(ar.TryStart(MakeEntry(NewSub("Oxygen"), 1)));
        Assert.True(ar.TryStart(MakeEntry(NewSub("Power"), 1)));

        Assert.False(ar.TryStart(MakeEntry(NewSub("Shields"), 1)));
        Assert.Equal(2, ar.InFlightCount);
        Assert.Equal(0, ar.AvailableSlots);
    }

    [Fact]
    public void TryStart_SameSubsystemTwice_RejectsSecond()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        var oxygen = NewSub("Oxygen");

        Assert.True(ar.TryStart(MakeEntry(oxygen, 2)));
        // Second start on the same subsystem is rejected even though slots remain free.
        Assert.False(ar.TryStart(MakeEntry(oxygen, 1)));
        Assert.Equal(1, ar.InFlightCount);
        Assert.Equal(3, ar.AvailableSlots);
    }

    [Fact]
    public void AvailableSlots_TracksInFlightCountInverse()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        Assert.Equal(4, ar.AvailableSlots);

        ar.TryStart(MakeEntry(NewSub("Oxygen"), 1));
        ar.TryStart(MakeEntry(NewSub("Power"), 1));
        Assert.Equal(2, ar.InFlightCount);
        Assert.Equal(2, ar.AvailableSlots);

        ar.DecrementAll();
        ar.DrainCompleted();
        Assert.Equal(0, ar.InFlightCount);
        Assert.Equal(4, ar.AvailableSlots);
    }

    [Fact]
    public void DecrementAll_DecrementsEachEntry()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        ar.TryStart(MakeEntry(NewSub("Oxygen"), 3));
        ar.TryStart(MakeEntry(NewSub("Power"), 2));

        ar.DecrementAll();

        Assert.Equal(2, ar.InFlight[0].CyclesRemaining);
        Assert.Equal(1, ar.InFlight[1].CyclesRemaining);
    }

    [Fact]
    public void DrainCompleted_RemovesAndReturnsZeroCyclesRemaining()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        ar.TryStart(MakeEntry(NewSub("Oxygen"), 1));
        ar.TryStart(MakeEntry(NewSub("Power"), 2));
        ar.TryStart(MakeEntry(NewSub("Shields"), 1));

        ar.DecrementAll();

        var completed = ar.DrainCompleted();

        Assert.Equal(2, completed.Count);
        Assert.Contains(completed, e => e.Subsystem.Name == "Oxygen");
        Assert.Contains(completed, e => e.Subsystem.Name == "Shields");
        Assert.Equal(1, ar.InFlightCount);
        Assert.Equal("Power", ar.InFlight[0].Subsystem.Name);
    }

    [Fact]
    public void TryCancelOldestOn_OnMatchingSubsystem_RemovesOldest()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        var oxygen = NewSub("Oxygen");
        var power = NewSub("Power");
        var oldOxygen = MakeEntry(oxygen, 1);

        ar.TryStart(oldOxygen);
        ar.TryStart(MakeEntry(power, 1));

        var ok = ar.TryCancelOldestOn(oxygen, out var cancelled);

        Assert.True(ok);
        Assert.NotNull(cancelled);
        Assert.Same(oldOxygen.Subsystem, cancelled!.Subsystem);
        Assert.Equal(1, ar.InFlightCount);
        Assert.Equal("Power", ar.InFlight[0].Subsystem.Name);
    }

    [Fact]
    public void TryCancelOldestOn_NoMatch_ReturnsFalse()
    {
        var ar = new ActiveRepairs(concurrentRepairs: 4);
        var oxygen = NewSub("Oxygen");
        var power = NewSub("Power");
        ar.TryStart(MakeEntry(oxygen, 1));

        var ok = ar.TryCancelOldestOn(power, out var cancelled);

        Assert.False(ok);
        Assert.Null(cancelled);
        Assert.Equal(1, ar.InFlightCount);
    }
}
