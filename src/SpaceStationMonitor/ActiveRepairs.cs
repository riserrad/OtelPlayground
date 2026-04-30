using System.Diagnostics;

namespace SpaceStationMonitor;

public sealed record InFlightRepair(
    Subsystem Subsystem,
    int Requested,
    int CyclesRemaining,
    Activity? RepairAction);

public sealed class ActiveRepairs
{
    private readonly int _maxConcurrentRepairs;
    private readonly List<InFlightRepair> _entries = new();

    public ActiveRepairs(int concurrentRepairs)
    {
        _maxConcurrentRepairs = concurrentRepairs;
    }

    public int InFlightCount => _entries.Count;
    public int AvailableSlots => _maxConcurrentRepairs - _entries.Count;
    public IReadOnlyList<InFlightRepair> InFlight => _entries;

    // A repair starts only if a slot is free AND the subsystem isn't already under repair.
    // The same-subsystem block prevents stacking two hands on one repair, which would let
    // a player cheat the cycles-required commitment by re-pressing R every tick.
    public bool TryStart(InFlightRepair entry)
    {
        if (_entries.Count >= _maxConcurrentRepairs) return false;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Subsystem, entry.Subsystem)) return false;
        }
        _entries.Add(entry);
        return true;
    }

    public void DecrementAll()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            _entries[i] = e with { CyclesRemaining = e.CyclesRemaining - 1 };
        }
    }

    public IReadOnlyList<InFlightRepair> DrainCompleted()
    {
        var completed = new List<InFlightRepair>();
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].CyclesRemaining <= 0)
            {
                completed.Insert(0, _entries[i]);
                _entries.RemoveAt(i);
            }
        }
        return completed;
    }

    public bool TryCancelOldestOn(Subsystem target, out InFlightRepair? cancelled)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Subsystem, target))
            {
                cancelled = _entries[i];
                _entries.RemoveAt(i);
                return true;
            }
        }
        cancelled = null;
        return false;
    }
}
