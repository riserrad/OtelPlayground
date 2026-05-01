# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A C# console game where you manage a space station with degrading subsystems, cascade failures, and random events. The primary goal is learning OpenTelemetry instrumentation through gameplay.

## Tech Stack

- C# / .NET 10 (console application)
- OpenTelemetry SDK for .NET (traces, metrics, logs)
- OTLP export to localhost:4317

## Build & Run

```bash
# Build all projects
dotnet build

# Run tests
dotnet test

# Start the Aspire Dashboard (requires Docker)
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest

# Run the game (in a separate terminal)
cd src/SpaceStationMonitor
dotnet run

# Publish portable executables
dotnet publish src/SpaceStationMonitor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/SpaceStationMonitor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

Endpoint is overridable via `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Target Frameworks

- **SpaceStationMonitor + tests:** `net10.0`
- **ComplimentGenerator + OTelWizard:** `net8.0`

Don't unify these unintentionally — SpaceStationMonitor was bumped to 10 deliberately.

## Architecture

- **src/SpaceStationMonitor/** - The game. Console app with OTel instrumentation.
  - `Program.cs` - OTel pipeline setup (OTLP exporter), main loop, input handling, Ctrl+C shutdown
  - `Station.cs` - Station model with 4 subsystems (Oxygen, Power, Shields, Thermal), degradation, emergency power
  - `RepairSystem.cs` - Repair mechanics with intentional "leaky repair" bug (activates after ~2 min)
  - `CascadeEngine.cs` - Cascade failure detection: subsystems below 30% health increase degradation on others
  - `EventEngine.cs` - Random events (solar flare, micrometeorite, power surge) with severity levels
  - `GameDisplay.cs` - Console rendering with health bars, warnings, event messages
  - `Telemetry.cs` - Static OTel primitives: ActivitySource, Meter, 6 counters, 1 histogram
  - `GeneralSpaceStationException.cs` - Custom exception for repair system failures
  - `BugStrategies/` - 6 swappable bug strategies (LeakyRepair, LatencyInjection, SilentCounterCorruption, StickyCascadeMultiplier, WrongTargetDegradation, RetryStorm); one picked at random per run, exposed as `bug.strategy` resource attribute and initial tag on `StationCycle` so Sprint 003 samplers can see it.

- **src/OTelWizard/** - A standalone OTLP gRPC listener (reference code, not the primary way to view telemetry).
  - `Services/TraceService.cs`, `MetricsService.cs`, `LogsService.cs` - gRPC handlers
  - `TelemetryExplainer.cs` - Parses OTLP requests, explains OTel concepts
  - `ConsoleRenderer.cs` - Color-coded console output
  - `Protos/` - OTLP proto files from opentelemetry-proto v1.4.0, compiled via Grpc.Tools

- **tests/SpaceStationMonitor.Tests/** - Unit tests for Station, RepairSystem, and CascadeEngine.

**Span topology:**
```
StationCycle (root, one per cycle)
├── SubsystemTick × 4      (one per subsystem, with health.before/after tags)
├── CascadeCheck × N       (when any subsystem is critical; links to source SubsystemTick)
└── StationEvent           (on random events)

RepairAction (root, lives 1-3 cycles after press)
  ↑ each StationCycle while in-flight links to this Activity
    (cycle → repair causality without parent-of falsification)
```

`RepairAction` is started when a slot is claimed (`HandleRepair` → `RepairSystem.BeginRepair`) and stops on completion, player cancel, reject, or shutdown. It is a root span, never parented to `StationCycle` — every in-flight cycle emits an `ActivityLink` to its context. `CascadeCheck` is parented to `StationCycle` (same cycle) but adds an `ActivityLink` to its source `SubsystemTick` since the cascade is causally-from-but-not-a-child-of the tick.

- **Service name:** `SpaceStationMonitor`
- **Traces:** `StationCycle` (parent span per cycle), `SubsystemTick` (per subsystem), `RepairAction`, `CascadeCheck`, `StationEvent`
- **Counters:** `station.repairs.total`, `station.repairs.failed`, `station.repairs.denied`, `station.cascade.failures`, `station.events.total`, `station.cycles.total`
- **Histogram:** `station.repair.effectiveness` (percent, repair applied vs. requested)
- **Gauges:** `station.subsystem.health` (per subsystem), `station.hull.integrity` (overall)
- **Logs:** Structured logs via ILogger+OTel for degradation, repairs, cascades, events, game over
- **Export:** OTLP gRPC to `localhost:4317` (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`)
