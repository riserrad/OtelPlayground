# Compliment Generator with OTel Wizard — Design Spec

## Goal

Build a cross-platform .NET 8 console application that generates random compliments at timed intervals, with a companion sidecar app that receives and explains OpenTelemetry telemetry in real time. The primary purpose is learning OpenTelemetry instrumentation.

## Solution Structure

```
MyFirstOtelProject/
├── MyFirstOtelProject.sln
├── src/
│   ├── ComplimentGenerator/          # Main app — shows compliments, collects feedback
│   │   ├── ComplimentGenerator.csproj
│   │   ├── Program.cs
│   │   ├── ComplimentEngine.cs       # Loads compliments, picks random, tracks duplicates
│   │   ├── InteractionManager.cs     # Handles like/dislike input with timeout
│   │   ├── DailyReport.cs            # End-of-day summary
│   │   └── Telemetry.cs              # OTel setup (traces, metrics, logs, OTLP exporter)
│   └── OTelWizard/                   # Sidecar — receives and explains telemetry
│       ├── OTelWizard.csproj
│       ├── Program.cs
│       ├── OtlpReceiver.cs           # gRPC OTLP listener on localhost:4317
│       ├── TelemetryExplainer.cs     # Generates teaching explanations for each signal
│       └── ConsoleRenderer.cs        # Color-coded output formatting
└── data/
    └── compliments.txt               # ~1000 compliments, one per line, embedded resource
```

## App 1: ComplimentGenerator

### Compliment Engine
- Loads ~1000 compliments from `compliments.txt` (embedded resource in the assembly)
- One compliment per line in the file
- Maintains a `HashSet<int>` of indices shown today to prevent duplicates within a day
- Selects a random compliment at random intervals between 10 and 60 seconds
- All compliments must be respectful — no racist, sexist, or offensive content

### User Interaction
- After displaying a compliment, the user can press `L` to like or `D` to dislike
- Input is non-blocking: if the next compliment timer fires before the user responds, the evaluation is skipped
- On exit (`Ctrl+C`) or at midnight, prints a daily report:
  - Total compliments generated
  - Total liked
  - Total disliked
  - Total skipped (no response)

### OpenTelemetry Instrumentation
- **Service name**: `ComplimentGenerator`
- **Traces**: One span per compliment lifecycle — created when compliment is generated, ended when feedback is received or skipped. Span attributes include compliment index, compliment text (truncated), and feedback result.
- **Metrics**:
  - `compliments.generated` (counter) — total compliments shown
  - `compliments.liked` (counter) — total liked
  - `compliments.disliked` (counter) — total disliked
  - `compliments.skipped` (counter) — total skipped
  - `compliments.response_time` (histogram) — seconds between compliment shown and feedback received
- **Logs**: Structured logs (via ILogger + OTel) for app start, compliment shown, feedback received, daily report generated
- **Export**: OTLP gRPC to `localhost:4317` (configurable via environment variable `OTEL_EXPORTER_OTLP_ENDPOINT`)

## App 2: OTelWizard (Sidecar)

### OTLP Receiver
- Hosts a gRPC OTLP endpoint on `localhost:4317`
- Accepts traces, metrics, and logs from the main app
- Implemented using ASP.NET Core + Grpc.AspNetCore to handle the OTLP protobuf messages

### Teaching Display
- For each incoming telemetry signal, displays:
  1. The raw data in a human-readable format
  2. A "wizard" explanation of what the signal is, why it matters, and how you would query it in observability tools
- Explanations cover OTel concepts: spans, trace context, counters vs histograms, structured logs, resource attributes, etc.

### Console Rendering
- Color-coded by signal type:
  - Traces: cyan
  - Metrics: yellow
  - Logs: green
- Clear visual separators between entries
- Timestamps on all entries

### Aspire Dashboard Compatibility
- Since ComplimentGenerator just exports to a configurable OTLP endpoint, users can later point it at the .NET Aspire Dashboard (or any OTLP-compatible backend) with zero code changes — just set the environment variable.

## Cross-Platform

- Target `net8.0` (no platform-specific TFM)
- No Windows-specific APIs
- Publish commands:
  - Windows: `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
  - Linux: `dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true`

## How to Run

Terminal 1 (start the sidecar first):
```bash
cd src/OTelWizard
dotnet run
```

Terminal 2 (start the main app):
```bash
cd src/ComplimentGenerator
dotnet run
```

## Key NuGet Packages

### ComplimentGenerator
- `OpenTelemetry`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `OpenTelemetry.Extensions.Hosting`

### OTelWizard
- `Grpc.AspNetCore`
- `Google.Protobuf`
- `OpenTelemetry.Proto` (for deserializing OTLP protobuf messages)
