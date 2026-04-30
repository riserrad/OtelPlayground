using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SpaceStationMonitor.Achievements;
using SpaceStationMonitor.BugStrategies;
using SpaceStationMonitor.Sampling;

namespace SpaceStationMonitor;

/// <summary>
/// The per-cycle game loop, extracted from <c>Program.cs</c> top-level statements so it can
/// be exercised in-process by tests (see <c>TestModeTests</c>) without spawning a subprocess.
/// In normal play, <see cref="RunAsync"/> polls keyboard input and waits a randomised interval
/// before each cycle. Under <see cref="TestModeConfig.TestMode"/> it skips input polling and
/// uses <see cref="TestModeConfig.TickInterval"/> for the wait, exiting after
/// <see cref="TestModeConfig.MaxCycles"/> cycles when set.
/// </summary>
public sealed class GameLoop
{
    private readonly Station _station;
    private readonly RepairSystem _repairSystem;
    private readonly EventEngine _eventEngine;
    private readonly CascadeEngine _cascadeEngine;
    private readonly GameDisplay _display;
    private readonly Random _random;
    private readonly ILogger _logger;
    private readonly IBugStrategy _strategy;
    private readonly TestModeConfig _testConfig;
    private readonly AchievementSystem? _achievementSystem;
    private readonly Action? _onQuit;
    private readonly Func<char?>? _keyReader;
    private readonly HullThresholdSampler? _sampler;
    private readonly SampleRegimeIndicator? _indicator;

    private int _selectedSubsystem;

    public GameLoop(
        Station station,
        RepairSystem repairSystem,
        EventEngine eventEngine,
        CascadeEngine cascadeEngine,
        GameDisplay display,
        Random random,
        ILogger logger,
        IBugStrategy strategy,
        TestModeConfig testConfig,
        AchievementSystem? achievementSystem = null,
        Action? onQuit = null,
        Func<char?>? keyReader = null,
        HullThresholdSampler? sampler = null,
        SampleRegimeIndicator? indicator = null)
    {
        _station = station;
        _repairSystem = repairSystem;
        _eventEngine = eventEngine;
        _cascadeEngine = cascadeEngine;
        _display = display;
        _random = random;
        _logger = logger;
        _strategy = strategy;
        _testConfig = testConfig;
        _achievementSystem = achievementSystem;
        _onQuit = onQuit;
        _keyReader = keyReader;
        _sampler = sampler;
        _indicator = indicator;
    }

    public async Task RunAsync(CancellationToken shutdownToken)
    {
        _display.Render(_station, _selectedSubsystem, _indicator);

        try
        {
            while (!shutdownToken.IsCancellationRequested && _station.HullIntegrity > 0)
            {
                var cycleInterval = _testConfig.TickInterval ?? TimeSpan.FromSeconds(
                    _repairSystem.IsBugActive ? _random.Next(2, 5) : _random.Next(4, 9));

                using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken))
                {
                    waitCts.CancelAfter(cycleInterval);

                    if (_testConfig.TestMode)
                    {
                        try { await Task.Delay(cycleInterval, waitCts.Token); }
                        catch (OperationCanceledException) { }
                    }
                    else
                    {
                        await PollInputAsync(waitCts);
                    }
                }

                _display.SetRepairMessage(null);
                _display.SetAchievement(null);

                if (shutdownToken.IsCancellationRequested) break;

                bool isBugActive = _repairSystem.IsBugActive;

                // SamplingBlindSpot needs the cycle loop to wire its hostile sampler into the
                // ParentBased sampler each tick. Toggling on/off in lock-step with IsBugActive
                // makes activation observable as a sudden trace-volume drop. The HostileSampler
                // is a static singleton, so this assignment does not allocate per cycle.
                if (_sampler is not null)
                {
                    _sampler.OverrideSampler =
                        _repairSystem.Strategy is SamplingBlindSpotStrategy && isBugActive
                            ? SamplingBlindSpotStrategy.HostileSampler
                            : null;
                }

                var parentOverride = _strategy.OverrideStationCycleParent() ?? default;

                // Build the link list from in-flight repairs before StartActivity. Each
                // in-flight RepairAction Activity gets linked from the new StationCycle so
                // the trace tree carries "this cycle relates to that in-flight repair."
                var inFlightLinks = new List<ActivityLink>();
                foreach (var inFlight in _station.ActiveRepairs.InFlight)
                {
                    if (inFlight.RepairAction is { Context: { } ctx } && ctx != default)
                        inFlightLinks.Add(new ActivityLink(ctx));
                }

                using var cycleActivity = Telemetry.ActivitySource.StartActivity(
                    "StationCycle",
                    ActivityKind.Internal,
                    parentContext: parentOverride,
                    tags: new KeyValuePair<string, object?>[]
                    {
                        new("bug.strategy", _strategy.Name),
                        new("bug.active", isBugActive),
                        new("cycle.number", _station.CycleCount + 1),
                    },
                    links: inFlightLinks);

                _station.StartNewCycle(isBugActive);

                // Drain in-flight repairs: tick down remaining cycles, complete any that
                // hit zero. CompleteRepair stops the saved RepairAction Activity, applies
                // health, and emits the histogram + log.
                _station.ActiveRepairs.DecrementAll();
                foreach (var completed in _station.ActiveRepairs.DrainCompleted())
                {
                    var result = _repairSystem.CompleteRepair(completed);

                    double effectiveness = result.Requested > 0
                        ? (double)result.Applied / result.Requested * 100.0
                        : 0;
                    Telemetry.RepairEffectiveness.Record(effectiveness,
                        new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));
                    _station.RecordRepair(effectiveness);

                    if (!result.IsHealthy)
                    {
                        _logger.LogError("Repair leak on {Name}: requested {Requested}% applied {Applied}%",
                            result.SubsystemName, result.Requested, result.Applied);
                    }
                    else
                    {
                        _logger.LogInformation("Repair applied to {Name}: {Before:F1}% → {After:F1}%",
                            result.SubsystemName, result.HealthBefore, result.HealthAfter);
                    }

                    _display.SetRepairMessage(
                        $"{result.SubsystemName} repair complete: {result.HealthBefore:F0}% → {result.DisplayedAfter:F0}%");
                }

                // Tick activities are saved per-subsystem so the cascade loop can link a
                // cascade back to the tick that triggered it. Keyed by sub.Name (what the
                // cascade engine asks for via cascade.SourceSubsystem), not the redirected
                // actualTarget — under WrongTargetDegradationStrategy the two diverge.
                var tickActivities = new Dictionary<string, Activity?>();

                foreach (var sub in _station.Subsystems)
                {
                    using var tickActivity = Telemetry.ActivitySource.StartActivity("SubsystemTick");
                    var actualTarget = _strategy.RedirectDegradationTarget(sub, _station.Subsystems);
                    var healthBefore = actualTarget.Health;

                    _station.DegradeSubsystem(actualTarget);

                    var tickTags = new KeyValuePair<string, object?>[]
                    {
                        new("subsystem.name", actualTarget.Name),
                        new("health.before", Math.Round(healthBefore, 1)),
                        new("health.after", Math.Round(actualTarget.Health, 1)),
                        new("degradation.rate",
                            Math.Round(actualTarget.BaseDegradationRate * actualTarget.CascadeMultiplier, 2)),
                    };
                    foreach (var tag in _strategy.MutateTags("SubsystemTick", tickTags))
                        tickActivity?.SetTag(tag.Key, tag.Value);

                    tickActivities[sub.Name] = tickActivity;

                    _logger.LogInformation("Subsystem {Name}: {Before:F1}% → {After:F1}%",
                        actualTarget.Name, healthBefore, actualTarget.Health);
                }

                var cascades = _cascadeEngine.CheckAndApplyCascades(_station, isBugActive);

                var criticalSystems = _station.Subsystems.Where(s => s.IsCritical).Select(s => s.Name).ToArray();
                _display.SetWarning(criticalSystems.Length > 0
                    ? $"WARNING: {string.Join(", ", criticalSystems)} below critical!"
                    : null);

                foreach (var cascade in cascades)
                {
                    using var cascadeActivity = Telemetry.ActivitySource.StartActivity("CascadeCheck");

                    // Link cascade to the source-tick activity that triggered it. Parent-of
                    // would be wrong (the cascade isn't a child of the tick); a link carries
                    // the causal relationship without falsifying parentage.
                    if (cascadeActivity is not null
                        && tickActivities.TryGetValue(cascade.SourceSubsystem, out var sourceTick)
                        && sourceTick is not null
                        && sourceTick.Context != default)
                    {
                        cascadeActivity.AddLink(new ActivityLink(sourceTick.Context));
                    }

                    cascadeActivity?.SetTag("cascade.triggered", true);
                    cascadeActivity?.SetTag("source.subsystem", cascade.SourceSubsystem);
                    cascadeActivity?.SetTag("affected.subsystems",
                        string.Join(",", cascade.AffectedSubsystems));

                    Telemetry.CascadeFailures.Add(1,
                        new KeyValuePair<string, object?>("source.subsystem", cascade.SourceSubsystem),
                        new KeyValuePair<string, object?>("affected.subsystem",
                            string.Join(",", cascade.AffectedSubsystems)));

                    _station.CascadeCount++;
                    // IsAllDataRequested is the sampler's keep decision for this span. Reading
                    // it here counts how many cascade traces survived sampling, which is the
                    // gameplay reveal for SamplingBlindSpot (the lie is the ratio of recorded
                    // cascades, not the cascades themselves).
                    if (cascadeActivity != null && cascadeActivity.IsAllDataRequested)
                        _station.CascadesTracedCount++;

                    _logger.LogError("Cascade failure: {Source} → {Affected}",
                        cascade.SourceSubsystem, string.Join(", ", cascade.AffectedSubsystems));
                }

                var stationEvent = _eventEngine.TryGenerateEvent(isBugActive);
                if (stationEvent != null)
                {
                    using var eventActivity = Telemetry.ActivitySource.StartActivity("StationEvent");
                    eventActivity?.SetTag("event.type", stationEvent.Type.ToString());
                    eventActivity?.SetTag("event.severity", stationEvent.Severity.ToString());
                    eventActivity?.SetTag("subsystem.affected", stationEvent.AffectedSubsystem);

                    _eventEngine.ApplyEvent(stationEvent, _station);

                    if (stationEvent.Type == StationEventType.SolarFlare)
                        _station.RecordSolarFlare();

                    Telemetry.EventsTotal.Add(1,
                        new KeyValuePair<string, object?>("event.type", stationEvent.Type.ToString()),
                        new KeyValuePair<string, object?>("event.severity", stationEvent.Severity.ToString()));

                    _display.SetEvent(
                        $"EVENT: {stationEvent.Type} ({stationEvent.Severity}) hit {stationEvent.AffectedSubsystem}!");

                    _logger.LogInformation("Station event: {Type} (severity {Severity}) hit {Subsystem}",
                        stationEvent.Type, stationEvent.Severity, stationEvent.AffectedSubsystem);
                }
                else
                {
                    _display.SetEvent(null);
                }

                Telemetry.CyclesTotal.Add(_strategy.CycleCounterIncrement());
                _station.EndCycle();
                _achievementSystem?.CheckAndFire(_station, cycleActivity, _logger, _display);
                cycleActivity?.SetTag("hull.integrity", Math.Round(_station.HullIntegrity, 1));

                _logger.LogInformation("Station cycle {Cycle} complete — hull integrity {Hull:F1}%",
                    _station.CycleCount, _station.HullIntegrity);

                _display.Render(_station, _selectedSubsystem, _indicator);

                if (_testConfig.MaxCycles is int max && _station.CycleCount >= max) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via Ctrl+C
        }
    }

    private async Task PollInputAsync(CancellationTokenSource waitCts)
    {
        try
        {
            while (!waitCts.Token.IsCancellationRequested)
            {
                if (TryReadKey(out var key))
                {
                    if (HandleKeyPress(key))
                    {
                        // Q was pressed; _onQuit has cancelled the upstream shutdown source.
                        // Exit the polling loop immediately so the caller's outer loop sees
                        // shutdownToken.IsCancellationRequested and breaks out of the cycle loop.
                        return;
                    }
                }
                await Task.Delay(100, waitCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // waitCts elapsed for the cycle interval, or upstream shutdown fired.
        }
    }

    // Returns true when the key handler signals the polling loop should exit immediately
    // (currently only on 'Q', which cancels the upstream shutdown source via _onQuit).
    private bool HandleKeyPress(char key)
    {
        switch (key)
        {
            case '1': _selectedSubsystem = 0; _display.Render(_station, _selectedSubsystem, _indicator); break;
            case '2': _selectedSubsystem = 1; _display.Render(_station, _selectedSubsystem, _indicator); break;
            case '3': _selectedSubsystem = 2; _display.Render(_station, _selectedSubsystem, _indicator); break;
            case '4': _selectedSubsystem = 3; _display.Render(_station, _selectedSubsystem, _indicator); break;

            case 'R': HandleRepair(); break;
            case 'C': HandleCancelRepair(); break;
            case 'E': HandleEmergencyPower(); break;

            case 'Q':
                // Linked-token sources only propagate parent→child, so cancelling waitCts here
                // would NOT signal the root shutdown CTS; the cycle loop would resume into the
                // next tick. Route through an upstream callback wired to shutdownCts.Cancel().
                _onQuit?.Invoke();
                return true;
        }
        return false;
    }

    // Test seam: when _keyReader is non-null (set by tests), we read from that delegate
    // instead of touching Console.KeyAvailable / Console.ReadKey. In production both call
    // sites end up at the real Console; tests use the seam to pump synthetic keys.
    private bool TryReadKey(out char key)
    {
        if (_keyReader != null)
        {
            var k = _keyReader();
            if (k.HasValue)
            {
                key = char.ToUpperInvariant(k.Value);
                return true;
            }
            key = '\0';
            return false;
        }

        if (Console.KeyAvailable)
        {
            key = char.ToUpperInvariant(Console.ReadKey(intercept: true).KeyChar);
            return true;
        }
        key = '\0';
        return false;
    }

    private void HandleRepair()
    {
        var sub = _station.Subsystems[_selectedSubsystem];

        if (sub.Health >= 100)
        {
            _display.SetRepairMessage($"{sub.Name} is at full health.");
            _display.Render(_station, _selectedSubsystem, _indicator);
            return;
        }

        var requested = _repairSystem.GetRepairAmount();
        var entry = _repairSystem.BeginRepair(sub, requested);

        if (!_station.ActiveRepairs.TryStart(entry))
        {
            // BeginRepair already started a RepairAction Activity; on rejection the
            // unbacked Activity must be stopped immediately so it doesn't leak across
            // cycles. The reject-Activity carries requested+cycles_required tags but
            // never reaches CompleteRepair — that is the correct shape for "we tried,
            // no slot was free, no repair occurred."
            entry.RepairAction?.SetStatus(ActivityStatusCode.Error, "rejected");
            entry.RepairAction?.Stop();

            Telemetry.RepairsDenied.Add(1,
                new KeyValuePair<string, object?>("subsystem.name", sub.Name));
            _logger.LogInformation("Repair denied on {Name}: all hands busy", sub.Name);
            _display.SetRepairMessage("All hands busy.");
            _display.Render(_station, _selectedSubsystem, _indicator);
            return;
        }

        _logger.LogInformation("Repair started on {Name}: {Requested}% over {Cycles} cycle(s)",
            sub.Name, requested, entry.CyclesRemaining);
        _display.SetRepairMessage($"Started repair on {sub.Name}.");
        _display.Render(_station, _selectedSubsystem, _indicator);
    }

    private void HandleCancelRepair()
    {
        var sub = _station.Subsystems[_selectedSubsystem];
        if (_station.ActiveRepairs.TryCancelOldestOn(sub, out var cancelled) && cancelled is not null)
        {
            cancelled.RepairAction?.AddEvent(new ActivityEvent("RepairCancelled",
                tags: new ActivityTagsCollection
                {
                    { "cancellation.reason", "player_cancel" }
                }));
            cancelled.RepairAction?.SetStatus(ActivityStatusCode.Error, "cancelled");
            cancelled.RepairAction?.Stop();

            Telemetry.RepairsFailed.Add(1,
                new KeyValuePair<string, object?>("subsystem.name", sub.Name),
                new KeyValuePair<string, object?>("cancellation.reason", "player_cancel"));

            _display.SetRepairMessage($"Cancelled repair on {sub.Name}.");
        }
        else
        {
            _display.SetRepairMessage($"Nothing to cancel on {sub.Name}.");
        }
        _display.Render(_station, _selectedSubsystem, _indicator);
    }

    private void HandleEmergencyPower()
    {
        if (_station.UseEmergencyPower())
        {
            _logger.LogInformation("Emergency power used. Remaining: {Remaining}",
                _station.EmergencyPowerRemaining);
            _display.SetRepairMessage(
                $"Emergency power! All systems +10%. ({_station.EmergencyPowerRemaining} left)");
        }
        else
        {
            _display.SetRepairMessage("No emergency power remaining!");
        }
        _display.Render(_station, _selectedSubsystem, _indicator);
    }
}
