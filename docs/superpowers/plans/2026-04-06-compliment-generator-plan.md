# Compliment Generator + OTel Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build two cross-platform .NET 8 console apps — a compliment generator with OTel instrumentation, and a sidecar that receives and explains the telemetry.

**Architecture:** ComplimentGenerator runs a timed loop that shows compliments, accepts like/dislike feedback, and exports traces/metrics/logs via OTLP. OTelWizard hosts a gRPC OTLP endpoint, deserializes incoming telemetry, and displays it with teaching explanations. Both target `net8.0` with no platform-specific APIs.

**Tech Stack:** .NET 8, OpenTelemetry SDK 1.15.x, Grpc.AspNetCore 2.x, Google.Protobuf, OTLP proto files compiled via Grpc.Tools.

---

## File Map

### Solution root
| File | Action | Responsibility |
|------|--------|---------------|
| `MyFirstOtelProject.sln` | Create | Solution file referencing both projects |

### src/ComplimentGenerator/
| File | Action | Responsibility |
|------|--------|---------------|
| `ComplimentGenerator.csproj` | Create | Project file with NuGet refs, embedded resource |
| `Program.cs` | Create | Entry point, Generic Host setup, main loop |
| `ComplimentEngine.cs` | Create | Load compliments, random pick, dedup tracking |
| `InteractionManager.cs` | Create | Non-blocking keyboard input with cancellation |
| `DailyReport.cs` | Create | Track stats, format end-of-day report |
| `Telemetry.cs` | Create | ActivitySource, Meter, OTel pipeline config |
| `Data/compliments.txt` | Create | ~1000 compliments, one per line (embedded resource) |

### src/OTelWizard/
| File | Action | Responsibility |
|------|--------|---------------|
| `OTelWizard.csproj` | Create | Project file with Grpc refs, proto compilation |
| `Program.cs` | Create | ASP.NET Core host, gRPC service registration |
| `Services/TraceService.cs` | Create | gRPC handler for OTLP trace export |
| `Services/MetricsService.cs` | Create | gRPC handler for OTLP metrics export |
| `Services/LogsService.cs` | Create | gRPC handler for OTLP logs export |
| `TelemetryExplainer.cs` | Create | Teaching-oriented explanations per signal |
| `ConsoleRenderer.cs` | Create | Color-coded, formatted console output |
| `Protos/` | Create | OTLP proto files copied from opentelemetry-proto |

---

## Task 1: Solution scaffolding and project files

**Files:**
- Create: `MyFirstOtelProject.sln`
- Create: `src/ComplimentGenerator/ComplimentGenerator.csproj`
- Create: `src/OTelWizard/OTelWizard.csproj`

- [ ] **Step 1: Create solution and ComplimentGenerator project**

```bash
cd /c/dev/MyFirstOtelProject
dotnet new sln --name MyFirstOtelProject
mkdir -p src/ComplimentGenerator
dotnet new console -n ComplimentGenerator -o src/ComplimentGenerator --framework net8.0
dotnet sln add src/ComplimentGenerator/ComplimentGenerator.csproj
```

- [ ] **Step 2: Create OTelWizard project**

```bash
cd /c/dev/MyFirstOtelProject
mkdir -p src/OTelWizard
dotnet new web -n OTelWizard -o src/OTelWizard --framework net8.0
dotnet sln add src/OTelWizard/OTelWizard.csproj
```

Using `dotnet new web` because OTelWizard needs ASP.NET Core for Kestrel + gRPC hosting.

- [ ] **Step 3: Add NuGet packages to ComplimentGenerator**

```bash
cd /c/dev/MyFirstOtelProject/src/ComplimentGenerator
dotnet add package OpenTelemetry --version 1.15.1
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.15.1
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.15.1
```

- [ ] **Step 4: Add NuGet packages to OTelWizard**

```bash
cd /c/dev/MyFirstOtelProject/src/OTelWizard
dotnet add package Grpc.AspNetCore --version 2.76.0
dotnet add package Google.Protobuf --version 3.29.3
dotnet add package Grpc.Tools --version 2.69.0
```

Use the latest versions that are compatible with .NET 8 (check `dotnet add` succeeds; adjust versions if needed).

- [ ] **Step 5: Verify both projects build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MyFirstOtelProject.sln src/ComplimentGenerator/ src/OTelWizard/
git commit -m "feat: scaffold solution with ComplimentGenerator and OTelWizard projects"
```

---

## Task 2: Compliments data file (~1000 compliments)

**Files:**
- Create: `src/ComplimentGenerator/Data/compliments.txt`
- Modify: `src/ComplimentGenerator/ComplimentGenerator.csproj` (add embedded resource)

- [ ] **Step 1: Create compliments.txt**

Create `src/ComplimentGenerator/Data/compliments.txt` with ~1000 unique, respectful compliments — one per line. Categories to cover:
- Kindness and character (e.g., "Your kindness makes the world a better place")
- Intelligence and creativity (e.g., "The way you solve problems is truly impressive")
- Appearance and style (e.g., "Your smile lights up the room")
- Impact on others (e.g., "Everyone is better for having you in their life")
- Humor and warmth (e.g., "Your laugh is contagious in the best way")
- Strength and resilience (e.g., "Your strength through tough times inspires everyone around you")
- Partnership and love (e.g., "Being with you is my favorite place to be")

All compliments must be positive, respectful, and free of any racist, sexist, or offensive content.

- [ ] **Step 2: Configure as embedded resource in csproj**

Add to `src/ComplimentGenerator/ComplimentGenerator.csproj` inside `<Project>`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data\compliments.txt" />
</ItemGroup>
```

- [ ] **Step 3: Verify build still succeeds**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add src/ComplimentGenerator/Data/compliments.txt src/ComplimentGenerator/ComplimentGenerator.csproj
git commit -m "feat: add ~1000 compliments as embedded resource"
```

---

## Task 3: ComplimentEngine

**Files:**
- Create: `src/ComplimentGenerator/ComplimentEngine.cs`

- [ ] **Step 1: Implement ComplimentEngine**

```csharp
using System.Reflection;

namespace ComplimentGenerator;

public class ComplimentEngine
{
    private readonly List<string> _compliments;
    private readonly HashSet<int> _shownToday = new();
    private readonly Random _random = new();

    public ComplimentEngine()
    {
        _compliments = LoadCompliments();
    }

    public int TotalAvailable => _compliments.Count;
    public int ShownToday => _shownToday.Count;
    public bool HasRemaining => _shownToday.Count < _compliments.Count;

    public (string Text, int Index)? GetNextCompliment()
    {
        if (!HasRemaining)
            return null;

        int index;
        do
        {
            index = _random.Next(_compliments.Count);
        } while (_shownToday.Contains(index));

        _shownToday.Add(index);
        return (_compliments[index], index);
    }

    public TimeSpan GetNextInterval()
    {
        int seconds = _random.Next(10, 61); // 10-60 seconds
        return TimeSpan.FromSeconds(seconds);
    }

    public void ResetDaily()
    {
        _shownToday.Clear();
    }

    private static List<string> LoadCompliments()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("compliments.txt"));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var compliments = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                compliments.Add(trimmed);
        }

        return compliments;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ComplimentGenerator/ComplimentEngine.cs
git commit -m "feat: implement ComplimentEngine with embedded resource loading and dedup"
```

---

## Task 4: DailyReport

**Files:**
- Create: `src/ComplimentGenerator/DailyReport.cs`

- [ ] **Step 1: Implement DailyReport**

```csharp
namespace ComplimentGenerator;

public class DailyReport
{
    public int Generated { get; private set; }
    public int Liked { get; private set; }
    public int Disliked { get; private set; }
    public int Skipped { get; private set; }

    public void RecordGenerated() => Generated++;
    public void RecordLiked() => Liked++;
    public void RecordDisliked() => Disliked++;
    public void RecordSkipped() => Skipped++;

    public string Format()
    {
        return $"""

        ╔══════════════════════════════════╗
        ║        Daily Report              ║
        ╠══════════════════════════════════╣
        ║  Compliments generated: {Generated,5}     ║
        ║  Liked:                 {Liked,5}     ║
        ║  Disliked:              {Disliked,5}     ║
        ║  Skipped:               {Skipped,5}     ║
        ╚══════════════════════════════════╝
        """;
    }

    public void Reset()
    {
        Generated = 0;
        Liked = 0;
        Disliked = 0;
        Skipped = 0;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ComplimentGenerator/DailyReport.cs
git commit -m "feat: implement DailyReport for tracking compliment stats"
```

---

## Task 5: InteractionManager

**Files:**
- Create: `src/ComplimentGenerator/InteractionManager.cs`

- [ ] **Step 1: Implement InteractionManager**

```csharp
namespace ComplimentGenerator;

public enum FeedbackResult
{
    Liked,
    Disliked,
    Skipped
}

public class InteractionManager
{
    /// <summary>
    /// Waits for user to press L (like) or D (dislike) until the cancellation token fires.
    /// Returns Skipped if no valid input before cancellation.
    /// </summary>
    public async Task<FeedbackResult> WaitForFeedbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    switch (char.ToUpperInvariant(key.KeyChar))
                    {
                        case 'L':
                            return FeedbackResult.Liked;
                        case 'D':
                            return FeedbackResult.Disliked;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Timer fired before user responded
        }

        return FeedbackResult.Skipped;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ComplimentGenerator/InteractionManager.cs
git commit -m "feat: implement InteractionManager with non-blocking keyboard input"
```

---

## Task 6: OTel instrumentation setup

**Files:**
- Create: `src/ComplimentGenerator/Telemetry.cs`

- [ ] **Step 1: Implement Telemetry**

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ComplimentGenerator;

public static class Telemetry
{
    public const string ServiceName = "ComplimentGenerator";
    public const string ActivitySourceName = "ComplimentGenerator";
    public const string MeterName = "ComplimentGenerator";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Counters
    public static readonly Counter<long> ComplimentsGenerated =
        Meter.CreateCounter<long>("compliments.generated", description: "Total compliments shown");

    public static readonly Counter<long> ComplimentsLiked =
        Meter.CreateCounter<long>("compliments.liked", description: "Total compliments liked");

    public static readonly Counter<long> ComplimentsDisliked =
        Meter.CreateCounter<long>("compliments.disliked", description: "Total compliments disliked");

    public static readonly Counter<long> ComplimentsSkipped =
        Meter.CreateCounter<long>("compliments.skipped", description: "Total compliments skipped");

    // Histogram
    public static readonly Histogram<double> ResponseTime =
        Meter.CreateHistogram<double>("compliments.response_time", "s",
            description: "Time between compliment shown and feedback received");
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ComplimentGenerator/Telemetry.cs
git commit -m "feat: define OTel ActivitySource, Meter, counters, and histogram"
```

---

## Task 7: ComplimentGenerator Program.cs

**Files:**
- Create: `src/ComplimentGenerator/Program.cs`

- [ ] **Step 1: Implement Program.cs**

```csharp
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
        Console.WriteLine($"  💬 {text}");
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
                Console.WriteLine("     ✓ Liked!");
                Console.ResetColor();
                Telemetry.ComplimentsLiked.Add(1);
                Telemetry.ResponseTime.Record(stopwatch.Elapsed.TotalSeconds);
                report.RecordLiked();
                activity?.SetTag("compliment.feedback", "liked");
                logger.LogInformation("Compliment #{Index} liked after {Seconds:F1}s", index, stopwatch.Elapsed.TotalSeconds);
                break;

            case FeedbackResult.Disliked:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("     ✗ Disliked");
                Console.ResetColor();
                Telemetry.ComplimentsDisliked.Add(1);
                Telemetry.ResponseTime.Record(stopwatch.Elapsed.TotalSeconds);
                report.RecordDisliked();
                activity?.SetTag("compliment.feedback", "disliked");
                logger.LogInformation("Compliment #{Index} disliked after {Seconds:F1}s", index, stopwatch.Elapsed.TotalSeconds);
                break;

            case FeedbackResult.Skipped:
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("     ⏭ Skipped (no response)");
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
                Console.WriteLine($"  ⏳ Next compliment in {remaining.Minutes}m {remaining.Seconds}s...");
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
```

- [ ] **Step 2: Verify build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add src/ComplimentGenerator/Program.cs
git commit -m "feat: implement ComplimentGenerator main loop with OTel instrumentation"
```

---

## Task 8: OTelWizard — Proto files and gRPC service stubs

**Files:**
- Create: `src/OTelWizard/Protos/opentelemetry/proto/collector/trace/v1/trace_service.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/collector/metrics/v1/metrics_service.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/collector/logs/v1/logs_service.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/trace/v1/trace.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/metrics/v1/metrics.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/logs/v1/logs.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/common/v1/common.proto`
- Create: `src/OTelWizard/Protos/opentelemetry/proto/resource/v1/resource.proto`
- Modify: `src/OTelWizard/OTelWizard.csproj`

- [ ] **Step 1: Download OTLP proto files**

Download the proto files from the opentelemetry-proto repository (v1.4.0 or latest stable tag). Place them under `src/OTelWizard/Protos/` preserving the directory structure.

The 8 proto files needed:
1. `opentelemetry/proto/common/v1/common.proto`
2. `opentelemetry/proto/resource/v1/resource.proto`
3. `opentelemetry/proto/trace/v1/trace.proto`
4. `opentelemetry/proto/metrics/v1/metrics.proto`
5. `opentelemetry/proto/logs/v1/logs.proto`
6. `opentelemetry/proto/collector/trace/v1/trace_service.proto`
7. `opentelemetry/proto/collector/metrics/v1/metrics_service.proto`
8. `opentelemetry/proto/collector/logs/v1/logs_service.proto`

Download via curl from GitHub raw content or clone the repo and copy.

- [ ] **Step 2: Configure proto compilation in csproj**

Replace the contents of `src/OTelWizard/OTelWizard.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" Version="2.76.0" />
    <PackageReference Include="Google.Protobuf" Version="3.29.3" />
    <PackageReference Include="Grpc.Tools" Version="2.69.0" PrivateAssets="All" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos\opentelemetry\proto\collector\trace\v1\trace_service.proto"
              GrpcServices="Server"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\collector\metrics\v1\metrics_service.proto"
              GrpcServices="Server"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\collector\logs\v1\logs_service.proto"
              GrpcServices="Server"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\trace\v1\trace.proto"
              GrpcServices="None"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\metrics\v1\metrics.proto"
              GrpcServices="None"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\logs\v1\logs.proto"
              GrpcServices="None"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\common\v1\common.proto"
              GrpcServices="None"
              ProtoRoot="Protos" />
    <Protobuf Include="Protos\opentelemetry\proto\resource\v1\resource.proto"
              GrpcServices="None"
              ProtoRoot="Protos" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Verify build compiles the protos**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build src/OTelWizard/OTelWizard.csproj
```

Expected: Build succeeded. Generated C# files appear in `obj/`.

- [ ] **Step 4: Commit**

```bash
git add src/OTelWizard/Protos/ src/OTelWizard/OTelWizard.csproj
git commit -m "feat: add OTLP proto files and configure gRPC code generation"
```

---

## Task 9: OTelWizard — ConsoleRenderer

**Files:**
- Create: `src/OTelWizard/ConsoleRenderer.cs`

- [ ] **Step 1: Implement ConsoleRenderer**

```csharp
namespace OTelWizard;

public static class ConsoleRenderer
{
    public static void WriteTrace(string message)
    {
        WriteSection(ConsoleColor.Cyan, "TRACE", message);
    }

    public static void WriteMetric(string message)
    {
        WriteSection(ConsoleColor.Yellow, "METRIC", message);
    }

    public static void WriteLog(string message)
    {
        WriteSection(ConsoleColor.Green, "LOG", message);
    }

    public static void WriteWizard(string explanation)
    {
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"  🧙 {explanation}");
        Console.ResetColor();
    }

    public static void WriteSeparator()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ─────────────────────────────────────────────────────");
        Console.ResetColor();
    }

    private static void WriteSection(ConsoleColor color, string label, string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  [{DateTime.Now:HH:mm:ss}] ");
        Console.ForegroundColor = color;
        Console.Write($"[{label}] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/OTelWizard/ConsoleRenderer.cs
git commit -m "feat: implement color-coded console renderer for OTelWizard"
```

---

## Task 10: OTelWizard — TelemetryExplainer

**Files:**
- Create: `src/OTelWizard/TelemetryExplainer.cs`

- [ ] **Step 1: Implement TelemetryExplainer**

```csharp
using Opentelemetry.Proto.Collector.Trace.V1;
using Opentelemetry.Proto.Collector.Metrics.V1;
using Opentelemetry.Proto.Collector.Logs.V1;
using Opentelemetry.Proto.Trace.V1;
using Opentelemetry.Proto.Metrics.V1;
using Opentelemetry.Proto.Logs.V1;

namespace OTelWizard;

public static class TelemetryExplainer
{
    public static void ExplainTraces(ExportTraceServiceRequest request)
    {
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var serviceName = resourceSpans.Resource?.Attributes
                .FirstOrDefault(a => a.Key == "service.name")?.Value?.StringValue ?? "unknown";

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                foreach (var span in scopeSpans.Spans)
                {
                    var traceId = Convert.ToHexString(span.TraceId.ToByteArray()).ToLowerInvariant();
                    var spanId = Convert.ToHexString(span.SpanId.ToByteArray()).ToLowerInvariant();
                    var name = span.Name;
                    var durationMs = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1_000_000.0;

                    var attributes = string.Join(", ",
                        span.Attributes.Select(a => $"{a.Key}={a.Value?.StringValue ?? a.Value?.IntValue.ToString() ?? "?"}"));

                    ConsoleRenderer.WriteTrace($"Span: \"{name}\" | TraceID: {traceId[..16]}... | SpanID: {spanId[..16]}");

                    if (!string.IsNullOrEmpty(attributes))
                    {
                        ConsoleRenderer.WriteTrace($"  Attributes: {attributes}");
                    }

                    if (durationMs > 0)
                    {
                        ConsoleRenderer.WriteTrace($"  Duration: {durationMs:F1}ms");
                    }

                    ConsoleRenderer.WriteWizard(
                        $"A Span represents a unit of work in your app. This span \"{name}\" belongs to " +
                        $"service \"{serviceName}\". The TraceID groups related spans into a single trace. " +
                        $"In Jaeger or Aspire Dashboard, you'd search: service.name=\"{serviceName}\" to find this.");

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }

    public static void ExplainMetrics(ExportMetricsServiceRequest request)
    {
        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    var name = metric.Name;
                    var description = metric.Description;

                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.Sum:
                            var sum = metric.Sum;
                            foreach (var dp in sum.DataPoints)
                            {
                                var value = dp.AsInt != 0 ? dp.AsInt : dp.AsDouble;
                                ConsoleRenderer.WriteMetric($"Counter: \"{name}\" = {value} | {description}");
                                ConsoleRenderer.WriteWizard(
                                    $"This is a Counter metric — it only goes up. \"{name}\" tracks a cumulative total. " +
                                    $"Counters are perfect for things you want to count over time (requests, errors, compliments). " +
                                    $"In PromQL you'd query: rate({name.Replace(".", "_")}[5m]) to see the per-second rate.");
                            }
                            break;

                        case Metric.DataOneofCase.Histogram:
                            var histogram = metric.Histogram;
                            foreach (var dp in histogram.DataPoints)
                            {
                                ConsoleRenderer.WriteMetric(
                                    $"Histogram: \"{name}\" | Count: {dp.Count}, Sum: {dp.Sum:F2} | {description}");
                                ConsoleRenderer.WriteWizard(
                                    $"A Histogram records the distribution of values (like response times). " +
                                    $"\"{name}\" has recorded {dp.Count} observations with a sum of {dp.Sum:F2}. " +
                                    $"Histograms let you compute percentiles (p50, p99). " +
                                    $"In PromQL: histogram_quantile(0.95, rate({name.Replace(".", "_")}_bucket[5m]))");
                            }
                            break;

                        default:
                            ConsoleRenderer.WriteMetric($"{metric.DataCase}: \"{name}\" | {description}");
                            break;
                    }

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }

    public static void ExplainLogs(ExportLogsServiceRequest request)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var log in scopeLogs.LogRecords)
                {
                    var severity = log.SeverityText;
                    var body = log.Body?.StringValue ?? "(empty)";
                    var traceId = log.TraceId.IsEmpty
                        ? null
                        : Convert.ToHexString(log.TraceId.ToByteArray()).ToLowerInvariant();

                    var attributes = string.Join(", ",
                        log.Attributes.Select(a => $"{a.Key}={a.Value?.StringValue ?? a.Value?.IntValue.ToString() ?? "?"}"));

                    ConsoleRenderer.WriteLog($"[{severity}] {body}");

                    if (!string.IsNullOrEmpty(attributes))
                    {
                        ConsoleRenderer.WriteLog($"  Attributes: {attributes}");
                    }

                    if (traceId != null)
                    {
                        ConsoleRenderer.WriteLog($"  TraceID: {traceId[..16]}...");
                    }

                    var traceCorrelation = traceId != null
                        ? $" This log carries a TraceID, meaning you can jump from this log directly to the trace that produced it — this is called trace-log correlation."
                        : " This log has no TraceID — it was emitted outside of an active span.";

                    ConsoleRenderer.WriteWizard(
                        $"OTel structured logs carry metadata (severity, attributes, trace context) alongside the message." +
                        traceCorrelation +
                        $" In a log backend, you'd filter: severity=\"{severity}\" AND resource.service.name=\"ComplimentGenerator\"");

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }
}
```

Note: The exact generated namespace for the proto classes depends on the proto package declarations. The namespaces above (`Opentelemetry.Proto.Collector.Trace.V1`, etc.) match the OTel proto definitions. If the build produces different namespaces, adjust the `using` statements accordingly.

- [ ] **Step 2: Verify build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build src/OTelWizard/OTelWizard.csproj
```

Fix any namespace mismatches from the generated proto code.

- [ ] **Step 3: Commit**

```bash
git add src/OTelWizard/TelemetryExplainer.cs
git commit -m "feat: implement TelemetryExplainer with OTel teaching explanations"
```

---

## Task 11: OTelWizard — gRPC service implementations

**Files:**
- Create: `src/OTelWizard/Services/TraceService.cs`
- Create: `src/OTelWizard/Services/MetricsService.cs`
- Create: `src/OTelWizard/Services/LogsService.cs`

- [ ] **Step 1: Implement TraceService**

```csharp
using Grpc.Core;
using Opentelemetry.Proto.Collector.Trace.V1;

namespace OTelWizard.Services;

public class TraceServiceImpl : TraceService.TraceServiceBase
{
    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainTraces(request);
        return Task.FromResult(new ExportTraceServiceResponse());
    }
}
```

- [ ] **Step 2: Implement MetricsService**

```csharp
using Grpc.Core;
using Opentelemetry.Proto.Collector.Metrics.V1;

namespace OTelWizard.Services;

public class MetricsServiceImpl : MetricsService.MetricsServiceBase
{
    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainMetrics(request);
        return Task.FromResult(new ExportMetricsServiceResponse());
    }
}
```

- [ ] **Step 3: Implement LogsService**

```csharp
using Grpc.Core;
using Opentelemetry.Proto.Collector.Logs.V1;

namespace OTelWizard.Services;

public class LogsServiceImpl : LogsService.LogsServiceBase
{
    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainLogs(request);
        return Task.FromResult(new ExportLogsServiceResponse());
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/OTelWizard/Services/
git commit -m "feat: implement gRPC OTLP service handlers for traces, metrics, and logs"
```

---

## Task 12: OTelWizard — Program.cs

**Files:**
- Create: `src/OTelWizard/Program.cs`

- [ ] **Step 1: Implement Program.cs**

```csharp
using OTelWizard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(4317, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var app = builder.Build();

app.MapGrpcService<TraceServiceImpl>();
app.MapGrpcService<MetricsServiceImpl>();
app.MapGrpcService<LogsServiceImpl>();

Console.ForegroundColor = ConsoleColor.DarkMagenta;
Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║          OTel Wizard — Telemetry Viewer                 ║");
Console.WriteLine("║  Listening for OTLP on localhost:4317 (gRPC)            ║");
Console.WriteLine("║  Start ComplimentGenerator in another terminal.         ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

app.Run();
```

- [ ] **Step 2: Verify both projects build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/OTelWizard/Program.cs
git commit -m "feat: implement OTelWizard host with gRPC OTLP endpoint on port 4317"
```

---

## Task 13: Update CLAUDE.md and final build verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md with actual architecture and run instructions**

Update the Build & Run and Architecture Notes sections to reflect the actual project structure now that code exists.

- [ ] **Step 2: Full clean build**

```bash
cd /c/dev/MyFirstOtelProject
dotnet clean
dotnet build
```

Expected: Build succeeded, 0 warnings about our code (NuGet warnings are OK).

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with actual architecture and run instructions"
```
