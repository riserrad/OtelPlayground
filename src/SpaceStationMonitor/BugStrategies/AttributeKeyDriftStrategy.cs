namespace SpaceStationMonitor.BugStrategies;

/// <summary>
/// Emits the same logical attribute under inconsistent keys across cycles. While active,
/// odd-cycle emissions on targeted instruments rename the canonical <c>subsystem.name</c>
/// tag to <c>subsystem</c>; even cycles pass through. The bug is invisible in the UI; it
/// surfaces when an operator filters a metric or trace by attribute key in the dashboard
/// and gets roughly half the rows they expect.
///
/// Targeted instruments are the high-frequency subsystem-keyed emission points: the
/// <c>station.cycles.total</c> counter (in practice carries no <c>subsystem.name</c>, so
/// the rename is a no-op there; the wiring is symmetric across all three sites), the
/// <c>station.subsystem.health</c> observable gauge, and the <c>SubsystemTick</c> span
/// tag set. Every variant of the <c>subsystem</c> key actually emitted is recorded in
/// <see cref="ObservedKeys"/> so the game-over reveal can report the count.
/// </summary>
public sealed class AttributeKeyDriftStrategy : BugStrategyBase
{
    private const string CanonicalKey = "subsystem.name";
    private const string DriftedKey = "subsystem";

    private static readonly HashSet<string> TargetedInstruments = new()
    {
        "station.cycles.total",
        "station.subsystem.health",
        "SubsystemTick",
    };

    private readonly Func<int> _cycleProvider;
    private readonly HashSet<string> _observedKeys = new();
    private readonly object _observedKeysLock = new();

    public override string Name => "AttributeKeyDrift";

    /// <summary>
    /// Distinct tag-key variants emitted on targeted instruments since activation.
    /// Read at game-over to render the reveal. The collection returned is a snapshot;
    /// safe to enumerate without locking even while the strategy continues mutating.
    /// </summary>
    public IReadOnlyCollection<string> ObservedKeys
    {
        get
        {
            lock (_observedKeysLock)
            {
                return _observedKeys.ToArray();
            }
        }
    }

    public AttributeKeyDriftStrategy(
        string bugTarget,
        TimeSpan? activationDelay = null,
        Func<int>? cycleProvider = null)
        : base(bugTarget, activationDelay)
    {
        _cycleProvider = cycleProvider ?? (() => 0);
    }

    public override IEnumerable<KeyValuePair<string, object?>> MutateTags(
        string instrumentName,
        IEnumerable<KeyValuePair<string, object?>> tags)
    {
        if (!IsBugActive) return tags;
        if (!TargetedInstruments.Contains(instrumentName)) return tags;

        bool drift = _cycleProvider() % 2 != 0;

        if (!drift)
        {
            // Even cycle: pass through, but record the canonical key if this targeted
            // instrument actually carries it. ObservedKeys must reflect every variant
            // emitted on a targeted site, not just the drifted ones.
            foreach (var tag in tags)
            {
                if (tag.Key == CanonicalKey)
                {
                    RecordKey(CanonicalKey);
                    break;
                }
            }
            return tags;
        }

        // Odd cycle: rename the canonical key on this targeted instrument and record the
        // drifted variant. Other tags are passed through unchanged.
        var result = new List<KeyValuePair<string, object?>>();
        foreach (var tag in tags)
        {
            if (tag.Key == CanonicalKey)
            {
                result.Add(new KeyValuePair<string, object?>(DriftedKey, tag.Value));
                RecordKey(DriftedKey);
            }
            else
            {
                result.Add(tag);
            }
        }
        return result;
    }

    private void RecordKey(string key)
    {
        lock (_observedKeysLock)
        {
            _observedKeys.Add(key);
        }
    }
}
