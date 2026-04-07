# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A C# console application that generates random compliments at random intervals (1-10 minutes). The primary goal is learning OpenTelemetry instrumentation — the app is a vehicle for exploring OTel concepts.

## Key Requirements

- **Console app** with no installation — portable .exe
- Generates compliments automatically while running; no user prompt to trigger them
- User can like/dislike each compliment before the next one arrives; unanswered evaluations are skipped
- No duplicate compliments within a day
- End-of-day report: total generated, liked, disliked
- **OTel "wizard" sidebar**: when telemetry events occur, the app should explain what was emitted and how to query it

## Tech Stack

- C# / .NET (console application)
- OpenTelemetry SDK for .NET — traces, metrics, and logs
- Assume the user is a beginner with OpenTelemetry; explanations and teaching moments are part of the deliverable

## Build & Run

```bash
# Build both projects
dotnet build

# Run OTelWizard (sidecar) in Terminal 1 — start this first
cd src/OTelWizard
dotnet run

# Run ComplimentGenerator (main app) in Terminal 2
cd src/ComplimentGenerator
dotnet run

# Publish portable executables
dotnet publish src/ComplimentGenerator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/ComplimentGenerator -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## Architecture

Two-project solution:

- **src/ComplimentGenerator/** — Console app. Loads ~1000 compliments from an embedded resource, shows them at random 1-10 min intervals, collects like/dislike feedback, exports OTel traces/metrics/logs via OTLP to localhost:4317.
  - `ComplimentEngine.cs` — Loads compliments from embedded `Data/compliments.txt`, random selection with daily dedup
  - `InteractionManager.cs` — Non-blocking keyboard input (L/D keys) with CancellationToken timeout
  - `DailyReport.cs` — Tracks generated/liked/disliked/skipped counts, formats end-of-day summary
  - `Telemetry.cs` — Static OTel primitives: ActivitySource, Meter, 4 counters, 1 histogram
  - `Program.cs` — OTel pipeline setup (OTLP exporter), main loop, Ctrl+C handling

- **src/OTelWizard/** — ASP.NET Core app hosting gRPC OTLP receiver on localhost:4317. Receives telemetry and displays it with color-coded teaching explanations.
  - `Services/TraceService.cs`, `MetricsService.cs`, `LogsService.cs` — gRPC handlers
  - `TelemetryExplainer.cs` — Parses OTLP requests, explains OTel concepts (spans, counters, histograms, log correlation)
  - `ConsoleRenderer.cs` — Color-coded output (traces=cyan, metrics=yellow, logs=green, wizard=magenta)
  - `Protos/` — OTLP proto files from opentelemetry-proto v1.4.0, compiled via Grpc.Tools

## OTel Instrumentation

- **Service name:** `ComplimentGenerator`
- **Traces:** One span per compliment lifecycle (`ComplimentLifecycle`) with attributes: index, text, feedback
- **Metrics:** `compliments.generated`, `compliments.liked`, `compliments.disliked`, `compliments.skipped` (counters); `compliments.response_time` (histogram, seconds)
- **Logs:** Structured logs via ILogger+OTel for app start, compliment shown, feedback, daily report
- **Export:** OTLP gRPC to `localhost:4317` (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`)
