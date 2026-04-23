# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A learning playground for **OpenTelemetry instrumentation in .NET**. The repo hosts three apps in one solution (`MyFirstOtelProject.sln`), all exporting OTLP gRPC to `localhost:4317`:

- **SpaceStationMonitor** — console game that's the centerpiece of Ricardo's MeTA IFx→OTel presentation. Ships an intentional bug ("leaky repairs") designed to be diagnosed live via the metrics→traces→logs triage flow, then fixed with a one-line edit on stage.
- **ComplimentGenerator** — the original, simpler app. Shows random compliments at 1–10 min intervals, collects like/dislike feedback, emits basic OTel (counters + histogram + flat spans).
- **OTelWizard** — a sidecar that hosts a gRPC OTLP receiver on 4317 and explains each trace/metric/log in plain English, color-coded. Alternative to the Aspire Dashboard for teaching mode.

Assume the reader is an OTel beginner — teaching moments are part of the deliverable.

## Build, Test, Run

```bash
# Build everything
dotnet build

# Run tests (xUnit, currently only covers SpaceStationMonitor domain logic)
dotnet test
dotnet test --filter "FullyQualifiedName~RepairSystemTests"   # single class
dotnet test --filter "FullyQualifiedName~RepairSystemTests.RepairsLeakWhenBugIsActive"  # single test

# Teaching mode: start the wizard first (Terminal 1), then the app (Terminal 2)
cd src/OTelWizard && dotnet run
cd src/SpaceStationMonitor && dotnet run    # or src/ComplimentGenerator

# Visual mode: Aspire Dashboard instead of OTelWizard (requires Docker)
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
# Then browse http://localhost:18888 and run the app in another terminal

# Publish portable single-file exe
dotnet publish src/SpaceStationMonitor -c Release -r win-x64   --self-contained true -p:PublishSingleFile=true
dotnet publish src/SpaceStationMonitor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

Endpoint is overridable via `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Target Frameworks

- **SpaceStationMonitor + tests:** `net10.0`
- **ComplimentGenerator + OTelWizard:** `net8.0`

Don't unify these unintentionally — SpaceStationMonitor was bumped to 10 deliberately.

## Architecture

### src/SpaceStationMonitor/ — the presentation demo

Top-level-statements `Program.cs` drives a game loop: player manages 4 subsystems (Oxygen / Power / Shields / Thermal), each degrades per cycle, and must be repaired before hull integrity reaches 0.

- `Program.cs` — OTel pipeline setup (traces + metrics + logs, OTLP exporter), splash gate, main loop, input handling, `HandleRepair`/`HandleEmergencyPower` helpers. Observable gauges are registered here (not in `Telemetry.cs`) because they close over the `Station` instance.
- `Telemetry.cs` — Static OTel primitives: `ActivitySource`, `Meter`, 5 counters, 1 histogram. Service/source/meter name = `SpaceStationMonitor`.
- `Station.cs` — Subsystems, degradation math, cycle counter, difficulty multiplier that ramps post-bug-activation. `HullIntegrity` is derived (avg of clamped subsystem health).
- `RepairSystem.cs` — **Contains the intentional bug.** See "The Leaky Repair Bug" below — do not refactor this file without reading that section.
- `CascadeEngine.cs` — Any subsystem below 30% multiplies neighbors' degradation rates next cycle.
- `EventEngine.cs` — Random solar flare / micrometeorite / power surge events. Bug-active mode skews severity higher and fires more often.
- `GameDisplay.cs` — ASCII console render: health bars, warnings, event line. Displays the *reported* repair result (the lie) — the truth lives only in telemetry.
- `GeneralSpaceStationException.cs` — Thrown on hard-zero repair failures; recorded on the `RepairAction` span via `AddException` + `SetStatus(Error)`.

**Span hierarchy per cycle:**
```
StationCycle (root)
├── SubsystemTick × 4      (one per subsystem, with health.before/after tags)
├── RepairAction           (only on player repair; span events on leak/failure)
├── CascadeCheck           (when any subsystem is critical)
└── StationEvent           (on random events)
```

**Metrics:** `station.subsystem.health` + `station.hull.integrity` (observable gauges), `station.repairs.total|failed` / `station.cascade.failures` / `station.events.total` / `station.cycles.total` (counters), `station.repair.effectiveness` (histogram — the metric that catches the bug via bimodal distribution).

### src/ComplimentGenerator/ — the simpler demo

- Loads ~1000 compliments from embedded `Data/compliments.txt`, picks one at a random 1–10 min interval, waits for L/D keypress, skips if the next compliment arrives first.
- `ComplimentEngine.cs` handles dedup-per-day. `InteractionManager.cs` does non-blocking input with a `CancellationToken` timeout. `DailyReport.cs` produces the end-of-day summary.
- Service name: `ComplimentGenerator`. Single flat span per compliment (`ComplimentLifecycle`); 4 counters + 1 histogram (`compliments.response_time`).

### src/OTelWizard/ — the teaching sidecar

- ASP.NET Core gRPC server on `localhost:4317` implementing the 3 OTLP collector services.
- `Services/{Trace,Metrics,Logs}Service.cs` — gRPC handlers; hand off to `TelemetryExplainer` which prints color-coded explanations.
- `TelemetryExplainer.cs` — Parses OTLP requests and narrates what OTel concepts each one demonstrates (spans, counters, histograms, trace↔log correlation via TraceID).
- `Protos/` — opentelemetry-proto v1.4.0, compiled via Grpc.Tools at build time. Don't hand-edit.
- Color convention: traces=cyan, metrics=yellow, logs=green, wizard commentary=magenta.

## The Leaky Repair Bug (presentation-critical)

`RepairSystem.cs:26-39` is **the whole point of the SpaceStationMonitor demo**. Two blocks sit side-by-side:

```csharp
// BUG: Repair leak — to fix, just uncomment the FIX line below.
int applied;
if (IsBugActive && subsystem.Name == _bugTargetSubsystem)
    applied = CalculateLeakyRepair(requested);
else
    applied = requested;

// FIX: Uncomment the line below to override the buggy value.
// applied = requested;
```

The bug activates ~2 minutes after startup (configurable via `RepairSystem` ctor) on a random subsystem chosen in `Program.cs`. Once active:
- 90% of repairs on that subsystem apply only 15–22% of requested
- 10% throw `GeneralSpaceStationException` (hard-zero), but only after ≥2 leaky repairs
- The display shows `DisplayedAfter` (what should have happened) while health uses `HealthAfter` (what actually happened). The divergence is observable only in telemetry.

**Do not "clean up" this code.** The side-by-side BUG/FIX shape is deliberate so a live fix is one uncomment-and-restart on stage. If you need to refactor, preserve the one-edit fix affordance.

## Docs & Plans

Authoritative design docs live in `docs/superpowers/`:

- `specs/2026-04-06-compliment-generator-design.md`
- `specs/2026-04-07-space-station-monitor-design.md` — full telemetry table and the 5-step debugging-story triage flow for the presentation
- `plans/` — corresponding implementation plans

Consult the SpaceStationMonitor design spec before changing telemetry shape, cycle timing, or the bug activation behavior — all of those are calibrated for the live demo.
