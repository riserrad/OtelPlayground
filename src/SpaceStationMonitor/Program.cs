using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;

// ── Splash gate ─────────────────────────────────────────────────────────────
using var shutdownCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

// Drain any keys buffered before render so stray input doesn't skip the splash.
while (Console.KeyAvailable) Console.ReadKey(intercept: true);

GameDisplay.RenderStartScreen();

bool quitFromSplash = false;
try
{
    while (!shutdownCts.Token.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var splashKey = Console.ReadKey(intercept: true);
            if (char.ToUpperInvariant(splashKey.KeyChar) == 'Q')
            {
                quitFromSplash = true;
            }
            break;
        }
        await Task.Delay(50, shutdownCts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C during splash
}

if (quitFromSplash || shutdownCts.IsCancellationRequested)
{
    return;
}

// ── Game components — clocks (Station.StartTime, strategy start time)
//    capture DateTime.UtcNow at construction, so they must be built AFTER the splash.
var station = new Station();
var random = new Random();
var bugTarget = station.Subsystems[random.Next(station.Subsystems.Length)].Name;
var strategies = BugStrategyCatalog.All(bugTarget);
var strategy = strategies[random.Next(strategies.Length)];
var repairSystem = new RepairSystem(strategy);
var eventEngine = new EventEngine();
var cascadeEngine = new CascadeEngine();
var display = new GameDisplay();
int selectedSubsystem = 0;

// ── OTel setup ──────────────────────────────────────────────────────────────
// bug.strategy goes on the resource so it's filterable across all signals
// (traces, metrics, logs) — not just on the StationCycle span.
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(Telemetry.ServiceName)
    .AddAttributes(new[] { new KeyValuePair<string, object>("bug.strategy", strategy.Name) });

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(Telemetry.ActivitySourceName)
    .AddOtlpExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(Telemetry.MeterName)
    .AddOtlpExporter()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddOpenTelemetry(logging =>
    {
        logging.SetResourceBuilder(resourceBuilder);
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
        logging.AddOtlpExporter();
    });
});

var logger = loggerFactory.CreateLogger("SpaceStationMonitor");

// ── Register gauge metrics (need Station reference) ─────────────────────────
Telemetry.Meter.CreateObservableGauge("station.subsystem.health",
    () => station.Subsystems.Select(s =>
        new Measurement<double>(s.Health,
            new KeyValuePair<string, object?>("subsystem.name", s.Name))),
    "percent", "Current health of each subsystem");

Telemetry.Meter.CreateObservableGauge<double>("station.hull.integrity",
    () => new Measurement<double>(station.HullIntegrity),
    "percent", "Overall station hull integrity");

logger.LogInformation("Bug strategy: {Name}, target: {Target}",
    strategy.Name, strategy.BugTargetSubsystem);

// Show the pristine station before the first cycle degrades it.
display.Render(station);

// ── Main loop ───────────────────────────────────────────────────────────────
try
{
    while (!shutdownCts.IsCancellationRequested && station.HullIntegrity > 0)
    {
        // ── Wait for input or next cycle (player sees current state) ──
        // Pre-bug: 4-8s (fun rhythm). Post-bug: 2-4s (player loses tempo).
        var cycleInterval = TimeSpan.FromSeconds(
            repairSystem.IsBugActive ? random.Next(2, 5) : random.Next(4, 9));
        using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token))
        {
            waitCts.CancelAfter(cycleInterval);
            try
            {
                while (!waitCts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        switch (char.ToUpperInvariant(key.KeyChar))
                        {
                            case '1': selectedSubsystem = 0; display.Render(station); break;
                            case '2': selectedSubsystem = 1; display.Render(station); break;
                            case '3': selectedSubsystem = 2; display.Render(station); break;
                            case '4': selectedSubsystem = 3; display.Render(station); break;

                            case 'R':
                                HandleRepair(station, repairSystem, selectedSubsystem, display, logger);
                                break;

                            case 'E':
                                HandleEmergencyPower(station, display, logger);
                                break;

                            case 'Q':
                                shutdownCts.Cancel();
                                break;
                        }
                    }
                    await Task.Delay(100, waitCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Wait timer expired or shutdown
            }
        }

        display.SetRepairMessage(null);

        if (shutdownCts.IsCancellationRequested) break;

        // ── Start cycle span ──
        // Snapshot bug state once per cycle so all phases of the tick see the same value.
        bool isBugActive = repairSystem.IsBugActive;

        // bug.strategy, bug.active, cycle.number are initial tags so samplers
        // (Sprint 003) can make decisions before the span is recorded.
        using var cycleActivity = Telemetry.ActivitySource.StartActivity(
            "StationCycle",
            ActivityKind.Internal,
            parentContext: default,
            tags: new KeyValuePair<string, object?>[]
            {
                new("bug.strategy", strategy.Name),
                new("bug.active", isBugActive),
                new("cycle.number", station.CycleCount + 1),
            });

        station.StartNewCycle(isBugActive);

        // ── Subsystem degradation (one child span per subsystem) ──
        foreach (var sub in station.Subsystems)
        {
            using var tickActivity = Telemetry.ActivitySource.StartActivity("SubsystemTick");
            var healthBefore = sub.Health;

            station.DegradeSubsystem(sub);

            tickActivity?.SetTag("subsystem.name", sub.Name);
            tickActivity?.SetTag("health.before", Math.Round(healthBefore, 1));
            tickActivity?.SetTag("health.after", Math.Round(sub.Health, 1));
            tickActivity?.SetTag("degradation.rate",
                Math.Round(sub.BaseDegradationRate * sub.CascadeMultiplier, 2));

            logger.LogInformation("Subsystem {Name}: {Before:F1}% → {After:F1}%",
                sub.Name, healthBefore, sub.Health);
        }

        // ── Cascade check ──
        var cascades = cascadeEngine.CheckAndApplyCascades(station, isBugActive);

        var criticalSystems = station.Subsystems.Where(s => s.IsCritical).Select(s => s.Name).ToArray();
        display.SetWarning(criticalSystems.Length > 0
            ? $"WARNING: {string.Join(", ", criticalSystems)} below critical!"
            : null);

        foreach (var cascade in cascades)
        {
            using var cascadeActivity = Telemetry.ActivitySource.StartActivity("CascadeCheck");
            cascadeActivity?.SetTag("cascade.triggered", true);
            cascadeActivity?.SetTag("source.subsystem", cascade.SourceSubsystem);
            cascadeActivity?.SetTag("affected.subsystems",
                string.Join(",", cascade.AffectedSubsystems));

            Telemetry.CascadeFailures.Add(1,
                new KeyValuePair<string, object?>("source.subsystem", cascade.SourceSubsystem),
                new KeyValuePair<string, object?>("affected.subsystem",
                    string.Join(",", cascade.AffectedSubsystems)));

            logger.LogError("Cascade failure: {Source} → {Affected}",
                cascade.SourceSubsystem, string.Join(", ", cascade.AffectedSubsystems));
        }

        // ── Random events ──
        var stationEvent = eventEngine.TryGenerateEvent(isBugActive);
        if (stationEvent != null)
        {
            using var eventActivity = Telemetry.ActivitySource.StartActivity("StationEvent");
            eventActivity?.SetTag("event.type", stationEvent.Type.ToString());
            eventActivity?.SetTag("event.severity", stationEvent.Severity.ToString());
            eventActivity?.SetTag("subsystem.affected", stationEvent.AffectedSubsystem);

            eventEngine.ApplyEvent(stationEvent, station);

            Telemetry.EventsTotal.Add(1,
                new KeyValuePair<string, object?>("event.type", stationEvent.Type.ToString()),
                new KeyValuePair<string, object?>("event.severity", stationEvent.Severity.ToString()));

            display.SetEvent(
                $"EVENT: {stationEvent.Type} ({stationEvent.Severity}) hit {stationEvent.AffectedSubsystem}!");

            logger.LogInformation("Station event: {Type} (severity {Severity}) hit {Subsystem}",
                stationEvent.Type, stationEvent.Severity, stationEvent.AffectedSubsystem);
        }
        else
        {
            display.SetEvent(null);
        }

        Telemetry.CyclesTotal.Add(strategy.CycleCounterIncrement());
        cycleActivity?.SetTag("hull.integrity", Math.Round(station.HullIntegrity, 1));

        logger.LogInformation("Station cycle {Cycle} complete — hull integrity {Hull:F1}%",
            station.CycleCount, station.HullIntegrity);

        // ── Render display (shown during next iteration's wait) ──
        display.Render(station);
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown via Ctrl+C
}

// ── Game over ───────────────────────────────────────────────────────────────
Console.Clear();
Console.ForegroundColor = station.HullIntegrity <= 0 ? ConsoleColor.Red : ConsoleColor.Cyan;
Console.WriteLine();
if (station.HullIntegrity <= 0)
{
    Console.WriteLine("╔══════════════════════════════════════════════════╗");
    Console.WriteLine("║             STATION DESTROYED                    ║");
    Console.WriteLine("╚══════════════════════════════════════════════════╝");
}
else
{
    Console.WriteLine("╔══════════════════════════════════════════════════╗");
    Console.WriteLine("║             SESSION ENDED                        ║");
    Console.WriteLine("╚══════════════════════════════════════════════════╝");
}
Console.WriteLine($"  Cycles survived: {station.CycleCount}");
Console.WriteLine($"  Final hull integrity: {station.HullIntegrity:F1}%");
Console.ResetColor();

logger.LogInformation("Game over — Cycles: {Cycles}, Hull: {Hull:F1}%",
    station.CycleCount, station.HullIntegrity);

// `using var` handles tracer/meter/logger provider disposal.

// ── Helper methods ──────────────────────────────────────────────────────────

void HandleRepair(Station station, RepairSystem repairSystem, int subsystemIndex,
    GameDisplay display, ILogger logger)
{
    var sub = station.Subsystems[subsystemIndex];

    if (!station.TryConsumeRepair())
    {
        Telemetry.RepairsDenied.Add(1,
            new KeyValuePair<string, object?>("subsystem.name", sub.Name));
        logger.LogInformation("Repair denied on {Name}: quota exhausted this cycle", sub.Name);
        display.SetRepairMessage("No repairs left this cycle — wait for next tick");
        display.Render(station);
        return;
    }

    var requested = repairSystem.GetRepairAmount();
    var currentHealth = sub.Health;
    var expectedHealth = Math.Min(100, currentHealth + requested);

    using var repairActivity = Telemetry.ActivitySource.StartActivity("RepairAction");

    try
    {
        var result = repairSystem.Repair(sub, requested);

        repairActivity?.SetTag("subsystem.name", result.SubsystemName);
        repairActivity?.SetTag("repair.requested", result.Requested);
        repairActivity?.SetTag("repair.applied", result.Applied);
        repairActivity?.SetTag("repair.healthy", result.IsHealthy);

        Telemetry.RepairsTotal.Add(1,
            new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

        double effectiveness = result.Requested > 0
            ? (double)result.Applied / result.Requested * 100.0
            : 0;
        Telemetry.RepairEffectiveness.Record(effectiveness,
            new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

        if (!result.IsHealthy)
        {
            // Leaky repair — record span event with delta
            repairActivity?.AddEvent(new ActivityEvent("RepairLeak",
                tags: new ActivityTagsCollection
                {
                        { "repair.delta", result.Requested - result.Applied }
                }));

            logger.LogError("Repair leak on {Name}: requested {Requested}% applied {Applied}%",
                result.SubsystemName, result.Requested, result.Applied);
        }
        else
        {
            logger.LogInformation("Repair applied to {Name}: {Before:F1}% → {After:F1}%",
                result.SubsystemName, result.HealthBefore, result.HealthAfter);
        }

        expectedHealth = result.DisplayedAfter; // The player sees the lie, not the reality
    }
    catch (Exception ex)
    {
        repairActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        repairActivity?.AddException(ex);
        repairActivity?.AddEvent(new ActivityEvent("RepairFailed"));

        Telemetry.RepairsFailed.Add(1,
                        new KeyValuePair<string, object?>("subsystem.name", sub.Name));

        logger.LogError(ex, "Repair failed on {Name}: requested {Requested}%",
                        sub.Name, requested);
    }

    // Display shows the lie - simulating accidental exception swallow in the repair system that hides the critical failure from the player.
    display.SetRepairMessage(
            $"Repaired {sub.Name}: {currentHealth:F0}% → {expectedHealth:F0}%");
    display.Render(station);
}

void HandleEmergencyPower(Station station, GameDisplay display, ILogger logger)
{
    if (station.UseEmergencyPower())
    {
        logger.LogInformation("Emergency power used. Remaining: {Remaining}",
            station.EmergencyPowerRemaining);
        display.SetRepairMessage(
            $"Emergency power! All systems +10%. ({station.EmergencyPowerRemaining} left)");
    }
    else
    {
        display.SetRepairMessage("No emergency power remaining!");
    }
    display.Render(station);
}
