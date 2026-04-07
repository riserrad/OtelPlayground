using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpaceStationMonitor;

// ── OTel setup ──────────────────────────────────────────────────────────────
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(Telemetry.ServiceName);

var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(Telemetry.ActivitySourceName)
    .AddOtlpExporter()
    .Build();

var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(Telemetry.MeterName)
    .AddOtlpExporter()
    .Build();

var loggerFactory = LoggerFactory.Create(builder =>
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

// ── Game components ─────────────────────────────────────────────────────────
var station = new Station();
var random = new Random();
var bugTarget = station.Subsystems[random.Next(station.Subsystems.Length)].Name;
var repairSystem = new RepairSystem(bugTarget);
var eventEngine = new EventEngine();
var cascadeEngine = new CascadeEngine();
var display = new GameDisplay();
var shutdownCts = new CancellationTokenSource();
int selectedSubsystem = 0;

// ── Register gauge metrics (need Station reference) ─────────────────────────
Telemetry.Meter.CreateObservableGauge("station.subsystem.health",
    () => station.Subsystems.Select(s =>
        new Measurement<double>(s.Health,
            new KeyValuePair<string, object?>("subsystem.name", s.Name))),
    "percent", "Current health of each subsystem");

Telemetry.Meter.CreateObservableGauge<double>("station.hull.integrity",
    () => new Measurement<double>(station.HullIntegrity),
    "percent", "Overall station hull integrity");

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

logger.LogInformation("Space Station Monitor started. Bug target: {Target} (activates after ~3 min)",
    repairSystem.BugTargetSubsystem);

// ── Main loop ───────────────────────────────────────────────────────────────
try
{
    while (!shutdownCts.IsCancellationRequested && station.HullIntegrity > 0)
    {
        // ── Start cycle span ──
        using var cycleActivity = Telemetry.ActivitySource.StartActivity("StationCycle");
        cycleActivity?.SetTag("cycle.number", station.CycleCount + 1);

        station.StartNewCycle();

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

            logger.LogInformation("Subsystem {Name}: {Before:F1}% \u2192 {After:F1}%",
                sub.Name, healthBefore, sub.Health);
        }

        // ── Cascade check ──
        var cascades = cascadeEngine.CheckAndApplyCascades(station);

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

            logger.LogError("Cascade failure: {Source} \u2192 {Affected}",
                cascade.SourceSubsystem, string.Join(", ", cascade.AffectedSubsystems));
        }

        // ── Random events ──
        var stationEvent = eventEngine.TryGenerateEvent();
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

        Telemetry.CyclesTotal.Add(1);
        cycleActivity?.SetTag("hull.integrity", Math.Round(station.HullIntegrity, 1));

        logger.LogInformation("Station cycle {Cycle} complete \u2014 hull integrity {Hull:F1}%",
            station.CycleCount, station.HullIntegrity);

        // ── Render display ──
        display.Render(station);

        // ── Wait for input or next cycle ──
        var cycleInterval = TimeSpan.FromSeconds(random.Next(5, 11));
        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);
        cycleCts.CancelAfter(cycleInterval);

        try
        {
            while (!cycleCts.Token.IsCancellationRequested)
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
                await Task.Delay(100, cycleCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Cycle timer expired or shutdown
        }

        display.SetRepairMessage(null);
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

logger.LogInformation("Game over \u2014 Cycles: {Cycles}, Hull: {Hull:F1}%",
    station.CycleCount, station.HullIntegrity);

// ── Cleanup ─────────────────────────────────────────────────────────────────
meterProvider?.Dispose();
tracerProvider?.Dispose();
loggerFactory?.Dispose();

// ── Helper methods ──────────────────────────────────────────────────────────

void HandleRepair(Station station, RepairSystem repairSystem, int subsystemIndex,
    GameDisplay display, ILogger logger)
{
    var sub = station.Subsystems[subsystemIndex];
    var requested = repairSystem.GetRepairAmount();

    using var repairActivity = Telemetry.ActivitySource.StartActivity("RepairAction");
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
        if (result.Applied == 0)
        {
            // Hard zero — record exception on the span
            var ex = new InvalidOperationException(
                $"Repair failed on {result.SubsystemName}: requested {result.Requested}% applied 0%");
            repairActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            repairActivity?.AddException(ex);
            repairActivity?.AddEvent(new ActivityEvent("RepairFailed"));

            Telemetry.RepairsFailed.Add(1,
                new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

            logger.LogError("Repair failed on {Name}: requested {Requested}% but applied 0%",
                result.SubsystemName, result.Requested);
        }
        else
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
    }
    else
    {
        logger.LogInformation("Repair applied to {Name}: {Before:F1}% \u2192 {After:F1}%",
            result.SubsystemName, result.HealthBefore, result.HealthAfter);
    }

    // Display shows the lie (full expected values, not actual)
    display.SetRepairMessage(
        $"Repaired {result.SubsystemName}: {result.HealthBefore:F0}% \u2192 {result.DisplayedAfter:F0}%");
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
