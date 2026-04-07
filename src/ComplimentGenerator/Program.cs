using System.Diagnostics;
using ComplimentGenerator;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

var logger = loggerFactory.CreateLogger("ComplimentGenerator");

// ── App components ──────────────────────────────────────────────────────────
var engine = new ComplimentEngine();
var interaction = new InteractionManager();
var report = new DailyReport();
var shutdownCts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

// ── Welcome ─────────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Magenta;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║          Compliment Generator v1.0                      ║");
Console.WriteLine("║  Press L to like, D to dislike. Ctrl+C to exit.        ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

logger.LogInformation("Application started. Loaded {Count} compliments.", engine.TotalAvailable);

// ── Main loop ───────────────────────────────────────────────────────────────
try
{
    while (!shutdownCts.IsCancellationRequested && engine.HasRemaining)
    {
        // Pick next compliment
        var compliment = engine.GetNextCompliment();
        if (compliment is null) break;

        var (text, index) = compliment.Value;

        // Start a trace span for this compliment lifecycle
        using var activity = Telemetry.ActivitySource.StartActivity("ComplimentLifecycle");
        activity?.SetTag("compliment.index", index);
        activity?.SetTag("compliment.text", text.Length > 80 ? text[..80] + "..." : text);

        // Show compliment
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  {text}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("     [L] Like  [D] Dislike");
        Console.ResetColor();

        Telemetry.ComplimentsGenerated.Add(1);
        report.RecordGenerated();
        logger.LogInformation("Compliment #{Index} shown: {Text}", index, text);

        // Wait for feedback or next timer
        var interval = engine.GetNextInterval();
        using var feedbackCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);
        feedbackCts.CancelAfter(interval);

        var stopwatch = Stopwatch.StartNew();
        var feedback = await interaction.WaitForFeedbackAsync(feedbackCts.Token);
        stopwatch.Stop();

        // Record feedback
        switch (feedback)
        {
            case FeedbackResult.Liked:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("     Liked!");
                Console.ResetColor();
                Telemetry.ComplimentsLiked.Add(1);
                Telemetry.ResponseTime.Record(stopwatch.Elapsed.TotalSeconds);
                report.RecordLiked();
                activity?.SetTag("compliment.feedback", "liked");
                logger.LogInformation("Compliment #{Index} liked after {Seconds:F1}s", index, stopwatch.Elapsed.TotalSeconds);
                break;

            case FeedbackResult.Disliked:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("     Disliked");
                Console.ResetColor();
                Telemetry.ComplimentsDisliked.Add(1);
                Telemetry.ResponseTime.Record(stopwatch.Elapsed.TotalSeconds);
                report.RecordDisliked();
                activity?.SetTag("compliment.feedback", "disliked");
                logger.LogInformation("Compliment #{Index} disliked after {Seconds:F1}s", index, stopwatch.Elapsed.TotalSeconds);
                break;

            case FeedbackResult.Skipped:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("     Skipped (no response)");
                Console.ResetColor();
                Telemetry.ComplimentsSkipped.Add(1);
                report.RecordSkipped();
                activity?.SetTag("compliment.feedback", "skipped");
                logger.LogInformation("Compliment #{Index} skipped", index);
                break;
        }

        Console.WriteLine();

        // If shutdown wasn't requested and feedback came early, wait the remaining interval
        if (!shutdownCts.IsCancellationRequested && feedback != FeedbackResult.Skipped)
        {
            var remaining = interval - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Next compliment in {remaining.Minutes}m {remaining.Seconds}s...");
                Console.ResetColor();
                try
                {
                    await Task.Delay(remaining, shutdownCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown via Ctrl+C
}

// ── Daily report ────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Magenta;
Console.WriteLine(report.Format());
Console.ResetColor();

logger.LogInformation("Daily report — Generated: {Generated}, Liked: {Liked}, Disliked: {Disliked}, Skipped: {Skipped}",
    report.Generated, report.Liked, report.Disliked, report.Skipped);

// ── Cleanup ─────────────────────────────────────────────────────────────────
meterProvider?.Dispose();
tracerProvider?.Dispose();
loggerFactory?.Dispose();
