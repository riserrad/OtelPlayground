# Space Station Monitor

**Learn OpenTelemetry by keeping a space station alive.**

You're in charge of a space station with four subsystems: Oxygen, Power, Shields, and Thermal. They degrade over time. Random events hit you. Failures cascade. Your job is to keep the hull integrity above zero for as long as you can.

Oh, and the whole thing is wired up with OpenTelemetry. Every cycle, every repair, every cascade failure emits traces, metrics, and logs. You get to see exactly what observability looks like in a real (well, simulated) system.

## Quick Start

**You need:**
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/products/docker-desktop/) (strongly recommended for viewing telemetry)

Without Docker, the game runs fine on its own. You just won't be able to see the telemetry it emits. That's like playing with your eyes closed, so... Docker.

**Run it:**

```bash
# Terminal 1: start the Aspire Dashboard (so you can see the telemetry)
docker run --rm -it -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest

# Terminal 2: start the game
cd src/SpaceStationMonitor
dotnet run
```

Open http://localhost:18888 in your browser to explore traces, metrics, and logs as you play.

The game exports telemetry to `localhost:4317` via OTLP, and the Docker command maps that port to the Aspire Dashboard's receiver. No extra config needed.

If port 4317 is already in use (another collector, a previous Docker run), you'll need to stop whatever is using it first, or change the endpoint with the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.

## How to Play

| Key | Action |
|-----|--------|
| **1-4** | Select a subsystem |
| **R** | Repair the selected subsystem |
| **E** | Use emergency power (boosts all systems, limited uses) |
| **Q** or **Ctrl+C** | Quit |

Every few seconds, a new cycle runs. Subsystems lose health. Random events (solar flares, micrometeorites, power surges) can hit at any time. When a subsystem drops below 30% health, it triggers cascade failures that speed up degradation on everything else.

You get 3 emergency power charges. Use them wisely.

**Here's the thing:** something in the station isn't working the way it should. The game won't tell you what it is. But the telemetry will. Can you figure out what's going wrong by looking at the traces and metrics in the Aspire Dashboard?

## What Telemetry Gets Emitted

This is where the learning happens. The game emits telemetry using the [OpenTelemetry SDK for .NET](https://opentelemetry.io/docs/languages/dotnet/), exported via [OTLP](https://opentelemetry.io/docs/specs/otlp/) (the standard protocol for shipping observability data).

### Traces

Each game cycle creates a parent span with child spans for individual operations:

- **StationCycle** - one per game cycle, tags: cycle number, hull integrity, bug state
- **SubsystemTick** - one per subsystem per cycle, tags: health before/after, degradation rate
- **RepairAction** - when you press R, tags: subsystem name, repair requested vs. applied
- **CascadeCheck** - when a cascade failure triggers, tags: source and affected subsystems
- **StationEvent** - when a random event hits, tags: event type, severity, affected subsystem

### Metrics

The game tracks a handful of counters: `station.repairs.total`, `station.repairs.failed`, `station.cascade.failures`, `station.events.total`, and `station.cycles.total`. These count what you'd expect from the names.

There's also a histogram (`station.repair.effectiveness`) that records how much of each repair actually got applied vs. what was requested. A healthy repair scores 100%. If you see numbers below that... well, that's a clue.

Two gauges give you a live view: `station.subsystem.health` (per subsystem) and `station.hull.integrity` (overall).

### Logs

Structured logs via ILogger + OpenTelemetry for every meaningful event: subsystem degradation, repairs, cascade failures, random events, and game over stats.

## Project Structure

```
src/
  SpaceStationMonitor/    # The game. Console app with OTel instrumentation.
  OTelWizard/             # A standalone OTLP listener (reference/example code).
tests/
  SpaceStationMonitor.Tests/  # Unit tests for game mechanics.
```

## Building

```bash
# Build everything
dotnet build

# Run tests
dotnet test

# Publish a portable executable (pick your platform)
dotnet publish src/SpaceStationMonitor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/SpaceStationMonitor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/SpaceStationMonitor -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

## License

[MIT](LICENSE)
