# Space Station Monitor — Design Spec

**Date:** 2026-04-07
**Project:** MyFirstOtelProject
**Purpose:** A console game that teaches OpenTelemetry debugging workflows through an interactive space station management simulation.

## Goals

1. Generate high-volume, diverse telemetry (traces, metrics, logs) for OTel education
2. Teach the **metrics → traces → logs** triage workflow through an intentional bug
3. Provide a live-fixable bug for presentation demos
4. Introduce OTel concepts not covered by ComplimentGenerator: nested spans, span events, exception recording, gauges, multi-dimensional metric attributes

## Gameplay

### Core Loop

The player manages a space station with 4 subsystems: **Oxygen**, **Power**, **Shields**, and **Thermal Control**. Each subsystem has a health level (0-100%) that degrades automatically over time.

**Auto-cycle (every 5-10 seconds):**

1. Each subsystem loses health based on its degradation rate + random variance
2. Subsystems below 30% trigger warnings
3. Subsystems at 0% trigger cascade failures — adjacent systems degrade faster
4. Hull integrity is derived from all subsystem health values

**Player interaction (minimal keypresses):**

- `1/2/3/4` — select a subsystem
- `R` — repair selected subsystem (restores 15-25% health)
- `E` — emergency power (boosts all systems by 10%, limited uses)
- `S` — status overview
- `Q` — quit

**Session flow:**

- Starts calm — slow degradation, easy to keep up
- Gradually increases difficulty (degradation accelerates)
- Random events: solar flare (shields drain), power surge, micrometeorite impact
- Game ends when hull integrity reaches 0% or player quits

### Display

```
╔══════════════════════════════════════════════════╗
║          SPACE STATION MONITOR v1.0              ║
║          Hull Integrity: 87%                     ║
╠══════════════════════════════════════════════════╣
║  [1] Oxygen          ████████████░░░░  72%       ║
║  [2] Power           ██████████████░░  89%       ║
║  [3] Shields         █████░░░░░░░░░░░  28%  ⚠   ║
║  [4] Thermal Control ████████████████  95%       ║
╠══════════════════════════════════════════════════╣
║  ⚠ WARNING: Shields below critical threshold!    ║
║  ☄ EVENT: Micrometeorite impact on Shields!      ║
╠══════════════════════════════════════════════════╣
║  [R] Repair   [E] Emergency Power (3 left)       ║
║  [S] Status   [Q] Quit                           ║
║  Cycle: 42  |  Uptime: 5m 32s                    ║
╚══════════════════════════════════════════════════╝
```

- Health bars give instant visual feedback
- Warning indicator when below 30%
- Event messages appear for one cycle then clear
- When the bug is active, the display shows **reported** repair values (which look normal) — the truth is only in telemetry

## The Bug: Leaky Repairs

### Behavior

After a configurable delay (default ~3 minutes), one randomly-chosen subsystem's repair logic becomes faulty:

- **90% of repairs**: "leaky" — applies only 20-30% of the intended repair value
- **10% of repairs**: "hard zero" — repair does nothing at all
- Hard zeroes only begin after at least 2 leaky repairs have occurred

The display lies: it shows the full expected repair value. The telemetry records the actual value.

### Code Structure (live-fix pattern)

The buggy code and fix live side-by-side in `RepairSystem.cs`:

```csharp
// BUG: Repair leak — uncomment the fix block and comment this block to resolve
int applied = CalculateLeakyRepair(requested);

// FIX: Correct repair logic
// int applied = requested;
```

During the presentation, the fix is: comment the buggy line, uncomment the fix line, restart.

### Debugging Story (the triage flow)

| Step | Signal | What to show | Teaches |
|------|--------|-------------- |---------|
| 1. Notice | `station.subsystem.health` gauge trending down despite repairs | Metrics tell you SOMETHING is wrong | Gauges, dashboards |
| 2. Correlate | `station.repair.effectiveness` histogram shows bimodal distribution | Metrics tell you WHAT is wrong | Histograms, percentiles |
| 3. Narrow down | `RepairAction` spans with `repair.applied << repair.requested` | Traces tell you WHERE it's wrong | Span attributes, filtering |
| 4. Root cause | ERROR log inside the bad span: repair leak details | Logs tell you WHY | Trace-log correlation via TraceID |
| 5. Fix live | Uncomment fix, restart, watch metrics recover | Telemetry confirms the fix | Closing the loop |

## Telemetry Instrumentation

### Service Name

`SpaceStationMonitor` — distinct from `ComplimentGenerator` so both can run simultaneously.

### Traces — Span Hierarchy

```
StationCycle (root span, every 5-10s)
├── SubsystemTick (child, ×4 — one per subsystem)
│   └── attrs: subsystem.name, health.before, health.after, degradation.rate
├── RepairAction (child, only when player repairs)
│   └── attrs: subsystem.name, repair.requested, repair.applied, repair.healthy
│   └── span event on leak: "RepairLeak" with repair.delta attribute
│   └── span event on hard zero: "RepairFailed" with exception recording
├── CascadeCheck (child, when any system < 30%)
│   └── attrs: cascade.triggered, source.subsystem, affected.subsystems
└── StationEvent (child, on random events)
    └── attrs: event.type, event.severity, subsystem.affected
```

### Metrics

| Metric | Type | Attributes | Purpose |
|--------|------|------------|---------|
| `station.subsystem.health` | Gauge | `subsystem.name` | Real-time health per subsystem |
| `station.hull.integrity` | Gauge | — | Overall station health |
| `station.repairs.total` | Counter | `subsystem.name` | Total repair attempts |
| `station.repairs.failed` | Counter | `subsystem.name` | Hard-zero failures |
| `station.repair.effectiveness` | Histogram | `subsystem.name` | Requested vs applied ratio — catches the leak |
| `station.cascade.failures` | Counter | `source.subsystem`, `affected.subsystem` | Cascade events |
| `station.events.total` | Counter | `event.type`, `event.severity` | Random events |
| `station.cycles.total` | Counter | — | Heartbeat / uptime |

### Logs (structured, trace-correlated)

- INFO: `"Station cycle {Cycle} complete — hull integrity {Hull}%"`
- INFO: `"Subsystem {Name}: {Before}% → {After}%"`
- WARN: `"{Name} below critical threshold: {Health}%"`
- INFO: `"Repair applied to {Name}: {Before}% → {After}%"`
- ERROR: `"Repair leak on {Name}: requested {Requested}% applied {Applied}%"`
- ERROR: `"Cascade failure: {Source} → {Affected}"`
- INFO: `"Station event: {Type} (severity {Severity}) hit {Subsystem}"`

### New OTel Concepts (beyond ComplimentGenerator)

| Concept | Where it appears |
|---------|------------------|
| Nested/child spans | Every cycle has child spans |
| Span events | Repair leak/failure recorded as events on the span |
| Exception recording | Hard-zero repairs record exceptions |
| Gauges | Health and hull integrity |
| Multi-dimensional attributes | Counters tagged by subsystem, event type |
| Metric-to-trace correlation | Histogram anomaly leads to finding matching spans |
| Trace-to-log correlation | Span TraceID leads to filtered logs with root cause |

## Architecture

### Project Structure

```
src/SpaceStationMonitor/
├── SpaceStationMonitor.csproj
├── Program.cs           — OTel pipeline setup, main game loop, Ctrl+C handling
├── Telemetry.cs         — Static OTel primitives: ActivitySource, Meter, all metrics
├── Station.cs           — 4 subsystems, health, degradation, hull integrity
├── RepairSystem.cs      — Repair logic (bug + commented fix live here)
├── EventEngine.cs       — Random events (solar flare, micrometeorite, power surge)
├── CascadeEngine.cs     — Cascade failure detection and propagation
└── GameDisplay.cs       — Console rendering: station status, warnings, events
```

### Integration

- Exports OTLP gRPC to `localhost:4317` (same as ComplimentGenerator)
- OTelWizard receives and explains telemetry from both apps without changes
- Added to `MyFirstOtelProject.sln` as a third project
- Same NuGet packages as ComplimentGenerator (OpenTelemetry, OTLP exporter)
- Same env var support (`OTEL_EXPORTER_OTLP_ENDPOINT`)

### Configuration

- Cycle interval: 5-10 seconds (randomized)
- Bug activation delay: configurable, default ~3 minutes
- Bug target subsystem: random at startup
- Degradation rates: tunable constants per subsystem
- Emergency power uses: default 3

### Running During Presentation

```bash
# Terminal 1: OTelWizard (start first — receives from all apps)
cd src/OTelWizard && dotnet run

# Terminal 2: SpaceStationMonitor (the star of the show)
cd src/SpaceStationMonitor && dotnet run

# Terminal 3 (optional): ComplimentGenerator
cd src/ComplimentGenerator && dotnet run
```

Start SpaceStationMonitor ~10 minutes before the talk to build baseline telemetry. The bug activates after ~3 minutes, giving a mix of healthy and unhealthy data to explore.
