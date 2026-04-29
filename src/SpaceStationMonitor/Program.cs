using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpaceStationMonitor;
using SpaceStationMonitor.Achievements;
using SpaceStationMonitor.BugStrategies;
using SpaceStationMonitor.Sampling;

// ── Test-mode flags (parsed once at startup) ────────────────────────────────
var testConfig = TestModeConfig.FromEnvironment(Environment.GetEnvironmentVariable);

// ── BUG_ACTIVATION_DELAY_MS (parsed once at startup) ────────────────────────
// Optional env-var override for the activation delay in Learning mode. Lets QA / CI
// exercise post-bug behavior without sitting through the production-tuned wait.
// Unset, empty, non-integer, or negative all fall back to the Learning-mode default.
var bugDelayRaw = Environment.GetEnvironmentVariable("BUG_ACTIVATION_DELAY_MS");
TimeSpan? bugActivationDelay = null;
bool bugDelayInvalid = false;
if (!string.IsNullOrEmpty(bugDelayRaw))
{
    if (int.TryParse(bugDelayRaw, out var bugDelayMs) && bugDelayMs >= 0)
        bugActivationDelay = TimeSpan.FromMilliseconds(bugDelayMs);
    else
        bugDelayInvalid = true;
}

// ── Splash gate ─────────────────────────────────────────────────────────────
using var shutdownCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

GameMode gameMode;
Difficulty difficulty;
if (testConfig.TestMode)
{
    (gameMode, difficulty) = SplashDefaults.Resolve(testConfig);
}
else
{
    var pickedMode = await PickModeAsync(shutdownCts.Token);
    if (pickedMode is null) return;
    gameMode = pickedMode.Value;

    var pickedDifficulty = await PickDifficultyAsync(gameMode, shutdownCts.Token);
    if (pickedDifficulty is null) return;
    difficulty = pickedDifficulty.Value;
}

// ── Per-mode + per-difficulty configuration ─────────────────────────────────
var tuning = DifficultyTunings.For(difficulty);
int repairsPerCycle = tuning.RepairsPerCycle;
double degradationMultiplier = tuning.DegradationMultiplier;
double eventChanceMultiplier = tuning.EventChanceMultiplier;

SamplerProfileKind profile;
TimeSpan modeActivationDelay;
string? samplerProfileInvalidRaw = null;
bool bugStrategyEnvIgnored = false;
bool samplerProfileEnvIgnored = false;

if (gameMode == GameMode.JustPlaying)
{
    profile = SamplerProfileKind.HullThreshold;
    modeActivationDelay = TimeSpan.MaxValue;

    bugStrategyEnvIgnored = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUG_STRATEGY"));
    samplerProfileEnvIgnored = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SAMPLER_PROFILE"));
}
else
{
    (profile, samplerProfileInvalidRaw) = ResolveLearningProfileFromEnv();
    modeActivationDelay = TimeSpan.FromMinutes(1);
}

// ── Game components ─────────────────────────────────────────────────────────
// Clocks (Station.StartTime, strategy start time) capture DateTime.UtcNow at
// construction, so these must be built AFTER the splash blocks for input.
var station = new Station(repairsPerCycle, degradationMultiplier);
var random = new Random();
var subsystemNames = station.Subsystems.Select(s => s.Name).ToArray();

string bugTarget;
IBugStrategy strategy;
if (gameMode == GameMode.JustPlaying)
{
    bugTarget = "(none)";
    strategy = new NoOpBugStrategy(bugTarget);
}
else
{
    try
    {
        (bugTarget, strategy) = BugSelector.Select(
            Environment.GetEnvironmentVariable, subsystemNames,
            bugActivationDelay ?? modeActivationDelay,
            cycleProvider: () => station.CycleCount + 1);
    }
    catch (InvalidOperationException ex)
    {
        // Narrow catch: the only InvalidOperationException BugSelector throws is
        // the unknown-BUG_STRATEGY one. Exit 2 so scripts can distinguish from
        // normal game exit (0) and a crash (nonzero other than 2).
        Console.Error.WriteLine(ex.Message);
        Environment.Exit(2);
        return;
    }
}

var repairSystem = new RepairSystem(strategy);
var eventEngine = new EventEngine(eventChanceMultiplier);
var cascadeEngine = new CascadeEngine(strategy);
var display = new GameDisplay();
var achievementSystem = new AchievementSystem();

// ── Export pipeline ─────────────────────────────────────────────────────────
// OTEL_TEST_CAPTURE=1 swaps the OTLP exporter for an InMemoryActivityExporter so
// headless smoke runs can prove tail-sampling gating empirically. The captured
// list is read at game-over and printed as `[CAPTURE] profile=... captured=...`.
bool useCapture = Environment.GetEnvironmentVariable("OTEL_TEST_CAPTURE") == "1";
InMemoryActivityExporter? captureExporter = useCapture ? new InMemoryActivityExporter() : null;

GatedBatchActivityExportProcessor? gatedBatch = null;
TailNextAdapter? adapter = null;
if (profile == SamplerProfileKind.Tail)
{
    BaseExporter<Activity> leafExporter = captureExporter ?? (BaseExporter<Activity>)
        new OtlpTraceExporter(new OtlpExporterOptions());
    gatedBatch = new GatedBatchActivityExportProcessor(leafExporter);
    adapter = new TailNextAdapter(gatedBatch);
}

var samplerProfile = SamplerProfileFactory.Build(
    profile,
    () => station.HullIntegrity,
    nextFactory: adapter is null ? null : () => adapter);
var indicator = new SampleRegimeIndicator(profile, samplerProfile.HullSampler);

// ── OTel setup ──────────────────────────────────────────────────────────────
// bug.strategy goes on the resource so it's filterable across all signals
// (traces, metrics, logs), not just on the StationCycle span.
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(Telemetry.ServiceName)
    .AddAttributes(new[] { new KeyValuePair<string, object>("bug.strategy", strategy.Name) });

var tracerBuilder = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(Telemetry.ActivitySourceName)
    .SetSampler(samplerProfile.HeadSampler);

if (gatedBatch is not null && samplerProfile.TailProcessor is not null)
{
    // Both processors are registered top-level so the SDK propagates SetParentProvider
    // to the gated batch (and through to the inner exporter for resource attribution).
    // Direct SDK OnEnd to the gated batch is no-op'd; tail-decided activities reach it
    // via TailNextAdapter, so only kept traces export.
    tracerBuilder.AddProcessor(gatedBatch);
    tracerBuilder.AddProcessor(samplerProfile.TailProcessor);
}
else if (captureExporter is not null)
{
    tracerBuilder.AddProcessor(new SimpleActivityExportProcessor(captureExporter));
}
else
{
    tracerBuilder.AddOtlpExporter();
}
using var tracerProvider = tracerBuilder.Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(Telemetry.MeterName)
    .SetExemplarFilter(ExemplarFilterType.TraceBased)
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

if (bugDelayInvalid)
{
    logger.LogWarning(
        "Ignoring invalid BUG_ACTIVATION_DELAY_MS={Value}; expected a non-negative integer. Using mode default.",
        bugDelayRaw);
}
if (samplerProfileInvalidRaw is not null)
{
    logger.LogWarning(
        "Ignoring invalid SAMPLER_PROFILE={Value}; expected hull|alwayson|tail|rules. Using default 'tail'.",
        samplerProfileInvalidRaw);
}
if (bugStrategyEnvIgnored)
{
    logger.LogInformation(
        "Ignoring BUG_STRATEGY env var: bug strategies disabled in Just Playing mode.");
}
if (samplerProfileEnvIgnored)
{
    logger.LogInformation(
        "Ignoring SAMPLER_PROFILE env var: HullThreshold profile fixed in Just Playing mode.");
}

// ── Register gauge metrics (need Station reference) ─────────────────────────
Telemetry.Meter.CreateObservableGauge("station.subsystem.health",
    () => station.Subsystems.Select(s =>
    {
        var rawTags = new[] { new KeyValuePair<string, object?>("subsystem.name", s.Name) };
        var mutated = strategy.MutateTags("station.subsystem.health", rawTags).ToArray();
        return new Measurement<double>(s.Health, mutated);
    }),
    "percent", "Current health of each subsystem");

Telemetry.Meter.CreateObservableGauge<double>("station.hull.integrity",
    () => new Measurement<double>(station.HullIntegrity),
    "percent", "Overall station hull integrity");

logger.LogInformation("Game mode: {Mode}, difficulty: {Difficulty}, sampler profile: {Profile}, bug strategy: {Strategy}, target: {Target}",
    gameMode, difficulty, profile, strategy.Name, strategy.BugTargetSubsystem);

// ── Run the game loop ───────────────────────────────────────────────────────
// onQuit is wired to the root shutdown CTS. Child linked tokens inside the loop's per-cycle
// wait don't propagate cancellation back up here, so the Q handler needs an upstream hook.
var gameLoop = new GameLoop(
    station, repairSystem, eventEngine, cascadeEngine, display, random, logger, strategy, testConfig,
    achievementSystem,
    onQuit: () => shutdownCts.Cancel(),
    sampler: samplerProfile.HullSampler,
    indicator: indicator);
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
Console.WriteLine($"  Score: {station.Score}");
Console.WriteLine($"  Achievements: {achievementSystem.UnlockedNames.Count} {string.Join(", ", achievementSystem.UnlockedNames)}");
if (gameMode == GameMode.Learning)
{
    Console.WriteLine($"  Bug strategy: {strategy.Name}  (target: {strategy.BugTargetSubsystem})");
    Console.WriteLine($"  Cascade failures: {station.CascadeCount,-3}  |   Traces captured: {station.CascadesTracedCount,-3}");
    Console.WriteLine("  See docs/bug-catalog-debugging.md for the bug catalog cheat-sheet.");
    if (repairSystem.Strategy is OrphanSpanStrategy orphan && orphan.InjectedCount > 0)
    {
        Console.WriteLine($"  Telemetry root-detection: {orphan.InjectedCount} synthetic remote-parented cycles");
    }
    if (repairSystem.Strategy is AttributeKeyDriftStrategy drift && drift.ObservedKeys.Count > 1)
    {
        Console.WriteLine($"  Telemetry attribute keys observed: {drift.ObservedKeys.Count} variants on 'subsystem'");
    }
}
Console.ResetColor();

logger.LogInformation("Game over. Cycles: {Cycles}, Hull: {Hull:F1}%",
    station.CycleCount, station.HullIntegrity);

if (captureExporter is not null)
{
    samplerProfile.TailProcessor?.ForceFlush(timeoutMilliseconds: 5000);
    tracerProvider.ForceFlush(timeoutMilliseconds: 5000);
    var gatedHasParent = gatedBatch is not null && gatedBatch.ParentProvider is not null;
    Console.WriteLine($"[CAPTURE] profile={profile} captured={captureExporter.Captured.Count} gatedBatchHasParent={(gatedBatch is null ? "n/a" : gatedHasParent.ToString())}");
}

// `using var` handles tracer/meter/logger provider disposal.

static (SamplerProfileKind profile, string? invalidRaw) ResolveLearningProfileFromEnv()
{
    var raw = Environment.GetEnvironmentVariable("SAMPLER_PROFILE");
    if (string.IsNullOrWhiteSpace(raw))
        return (SamplerProfileKind.Tail, null);
    return raw.Trim().ToLowerInvariant() switch
    {
        "hull" => (SamplerProfileKind.HullThreshold, null),
        "alwayson" => (SamplerProfileKind.AlwaysOn, null),
        "tail" => (SamplerProfileKind.Tail, null),
        "rules" => (SamplerProfileKind.Rules, null),
        _ => (SamplerProfileKind.Tail, raw),
    };
}

static async Task<GameMode?> PickModeAsync(CancellationToken token)
{
    while (Console.KeyAvailable) Console.ReadKey(intercept: true);
    GameDisplay.RenderModeScreen();

    try
    {
        while (!token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).KeyChar;
                if (SplashKeys.IsQuit(key)) return null;
                var picked = SplashKeys.TryParseMode(key);
                if (picked is not null) return picked;
            }
            else
            {
                await Task.Delay(50, token);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C during splash
    }

    return null;
}

static async Task<Difficulty?> PickDifficultyAsync(GameMode mode, CancellationToken token)
{
    while (Console.KeyAvailable) Console.ReadKey(intercept: true);
    GameDisplay.RenderDifficultyScreen(mode);

    try
    {
        while (!token.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).KeyChar;
                if (SplashKeys.IsQuit(key)) return null;
                var picked = SplashKeys.TryParseDifficulty(key);
                if (picked is not null) return picked;
            }
            else
            {
                await Task.Delay(50, token);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C during splash
    }

    return null;
}
