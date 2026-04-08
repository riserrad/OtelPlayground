# OpenTelemetry Learning Project

Learn OpenTelemetry by playing a game. This project teaches traces, metrics, and logs through two interactive console apps — with a sidecar that explains every piece of telemetry in plain English as it happens.

## Space Station Monitor

The highlight of this project. A real-time survival game where a space station is falling apart and you have to keep it alive.

```
╔══════════════════════════════════════════════════╗
║          SPACE STATION MONITOR v1.0              ║
║          Hull Integrity: 62%                     ║
╠══════════════════════════════════════════════════╣
║  [1] Oxygen           ████████████░░░░  75%      ║
║  [2] Power            ██████████░░░░░░  58%      ║
║  [3] Shields          █████░░░░░░░░░░░  28%  ⚠   ║
║  [4] Thermal Control  ████████████████  95%      ║
╠══════════════════════════════════════════════════╣
║  ⚠ CASCADE FAILURE: Shields critical!            ║
║  ☄ Solar Flare (Moderate) hit Shields -18%       ║
║  > Repaired Oxygen: +22%                         ║
╠══════════════════════════════════════════════════╣
║  [1-4] Select   [R] Repair   [E] Emergency Pwr  ║
║  [Q] Quit       Emergency Power: 2 left          ║
║  Cycle: 14     |  Uptime: 3m 42s                 ║
╚══════════════════════════════════════════════════╝
```

Four subsystems — Oxygen, Power, Shields, and Thermal Control — degrade every cycle. Random events (solar flares, micrometeorites, power surges) pile on damage. When any subsystem drops below 30%, cascade failures accelerate damage across the station. Hull integrity hits zero, game over.

### The Debugging Challenge

Here's the twist: after ~3 minutes, a bug activates. One subsystem's repairs start "leaking" — applying only 20-30% of the intended value, or sometimes nothing at all. The station starts dying faster than you can fix it.

This is intentional. The game teaches you to **find the bug using OpenTelemetry**:

1. **Metrics** — health stops recovering despite repairs
2. **Histograms** — repair effectiveness shows a bimodal split (100% vs 20-30%)
3. **Traces** — `RepairAction` spans reveal `repair.applied` diverging from `repair.requested`
4. **Logs** — ERROR-level entries explain exactly what's leaking

The fix is a single commented-out line in `RepairSystem.cs`. Uncomment it, restart, and watch the telemetry confirm the fix in real time.

## Compliment Generator

A gentler introduction. This app generates random compliments at 1-10 minute intervals. Like or dislike each one before the next arrives, and get a summary report at the end of the day.

- ~1,000 compliments loaded from an embedded resource, with daily deduplication
- Non-blocking keyboard input: **L** to like, **D** to dislike, or just wait to skip
- Emits traces (one span per compliment lifecycle), metrics (generated/liked/disliked/skipped counters, response time histogram), and structured logs

## OTelWizard (sidecar)

A companion app that receives telemetry via gRPC and explains it in plain English. Every trace, metric, and log gets a color-coded breakdown of what it means and why it matters. Start this first, then run either app in a second terminal.

## Running

### Option 1: OTelWizard (learning mode)

```bash
# Terminal 1 — start the telemetry explainer
cd src/OTelWizard
dotnet run

# Terminal 2 — pick an app
cd src/SpaceStationMonitor    # the game
dotnet run

# Or:
cd src/ComplimentGenerator    # the compliment app
dotnet run
```

The wizard displays color-coded explanations for each piece of telemetry as it arrives.

### Option 2: Aspire Dashboard (visual mode)

The [.NET Aspire Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/standalone) provides a full visual UI to explore traces, metrics, and logs. Requires Docker.

```bash
# Terminal 1 — start the dashboard
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest

# Terminal 2 — pick an app
cd src/SpaceStationMonitor
dotnet run
```

Then open http://localhost:18888 in your browser.

Both apps export to `localhost:4317` by default (configurable via `OTEL_EXPORTER_OTLP_ENDPOINT`), and the Docker command maps that port to the dashboard's OTLP receiver — no extra configuration needed.

## OTel Instrumentation

Both apps use the OpenTelemetry SDK for .NET and export via OTLP gRPC.

### Space Station Monitor

| Signal | What's emitted |
|--------|---------------|
| **Traces** | Nested span hierarchy: `StationCycle` → `SubsystemTick` (×4), `CascadeCheck`, `RepairAction`, `StationEvent` — each with detailed attributes |
| **Metrics** | `station.subsystem.health` and `station.hull.integrity` (gauges); repair, cascade, event, and cycle counters; `station.repair.effectiveness` (histogram) |
| **Logs** | Cycle summaries, degradation details, repair outcomes, cascade warnings, event impacts — all correlated by TraceID |

### Compliment Generator

| Signal | What's emitted |
|--------|---------------|
| **Traces** | One span per compliment lifecycle (`ComplimentLifecycle`) with attributes: index, text, feedback |
| **Metrics** | `compliments.generated`, `compliments.liked`, `compliments.disliked`, `compliments.skipped` (counters); `compliments.response_time` (histogram) |
| **Logs** | Structured logs for app start, compliment shown, feedback received, daily report |

## Project Structure

```
src/
├── SpaceStationMonitor/       # Space station survival game
│   ├── Program.cs             # OTel setup + game loop
│   ├── Telemetry.cs           # ActivitySource, Meter, 8 metrics
│   ├── Station.cs             # 4 subsystems, health, hull integrity
│   ├── RepairSystem.cs        # Repair logic + intentional bug
│   ├── EventEngine.cs         # Random events (solar flare, surge, meteorite)
│   ├── CascadeEngine.cs       # Cascade failure detection + propagation
│   └── GameDisplay.cs         # Console UI with health bars + warnings
│
├── ComplimentGenerator/       # Compliment app
│   ├── Program.cs             # OTel setup + main loop
│   ├── Telemetry.cs           # ActivitySource, Meter, counters, histogram
│   ├── ComplimentEngine.cs    # Loads compliments, random selection, daily dedup
│   ├── InteractionManager.cs  # Non-blocking keyboard input
│   ├── DailyReport.cs         # Generated/liked/disliked/skipped summary
│   └── Data/compliments.txt   # ~1,000 compliments (embedded resource)
│
└── OTelWizard/                # Telemetry sidecar
    ├── Program.cs             # ASP.NET Core + gRPC host on :4317
    ├── TelemetryExplainer.cs  # Parses OTLP, explains OTel concepts
    ├── ConsoleRenderer.cs     # Color-coded output
    ├── Services/              # gRPC handlers (Trace, Metrics, Logs)
    └── Protos/                # OTLP proto files (v1.4.0)
```

## Build

```bash
# Build all projects
dotnet build

# Publish portable executables
dotnet publish src/SpaceStationMonitor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/ComplimentGenerator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Tech Stack

- **C# / .NET 8.0** — console applications
- **OpenTelemetry SDK for .NET** — traces, metrics, and logs
- **OTLP (gRPC)** — standard wire format for exporting telemetry to any compatible backend
- **ASP.NET Core + gRPC** — powers the OTelWizard receiver
