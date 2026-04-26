using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpaceStationMonitor;
using SpaceStationMonitor.BugStrategies;

// ── Test-mode flags (parsed once at startup) ────────────────────────────────
var testConfig = TestModeConfig.FromEnvironment(Environment.GetEnvironmentVariable);

// ── Splash gate ─────────────────────────────────────────────────────────────
using var shutdownCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

if (!testConfig.TestMode)
{
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
}

// ── Game components — clocks (Station.StartTime, strategy start time)
//    capture DateTime.UtcNow at construction, so they must be built AFTER the splash.
var station = new Station();
var random = new Random();
var subsystemNames = station.Subsystems.Select(s => s.Name).ToArray();

string bugTarget;
IBugStrategy strategy;
try
{
    (bugTarget, strategy) = BugSelector.Select(Environment.GetEnvironmentVariable, subsystemNames);
}
catch (InvalidOperationException ex)
{
    // Narrow catch — the only InvalidOperationException BugSelector throws is
    // the unknown-BUG_STRATEGY one. Exit 2 so scripts can distinguish from
    // normal game exit (0) and a crash (nonzero other than 2).
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(2);
    return;
}

var repairSystem = new RepairSystem(strategy);
var eventEngine = new EventEngine();
var cascadeEngine = new CascadeEngine(strategy);
var display = new GameDisplay();

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

// ── Run the game loop ───────────────────────────────────────────────────────
var gameLoop = new GameLoop(
    station, repairSystem, eventEngine, cascadeEngine, display, random, logger, strategy, testConfig);
await gameLoop.RunAsync(shutdownCts.Token);

// ── Game over ───────────────────────────────────────────────────────────────
if (!Console.IsOutputRedirected)
{
    try { Console.Clear(); } catch (IOException) { }
}
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
