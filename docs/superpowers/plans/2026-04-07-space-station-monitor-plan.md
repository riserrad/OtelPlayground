# Space Station Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a console game where the player manages a space station's subsystems, generating rich OTel telemetry (nested spans, gauges, span events, exception recording) with an intentional bug that teaches the metrics → traces → logs debugging workflow.

**Architecture:** New `src/SpaceStationMonitor/` console app exporting OTLP to localhost:4317, received by the existing OTelWizard sidecar. Game logic is split into focused classes (Station, RepairSystem, EventEngine, CascadeEngine, GameDisplay) with telemetry orchestrated in Program.cs. A test project validates core game logic including the intentional bug behavior.

**Tech Stack:** C# / .NET 8.0, OpenTelemetry SDK 1.15.1, OTLP gRPC exporter, xUnit for tests

---

## File Structure

### New files to create

| File | Responsibility |
|------|----------------|
| `src/SpaceStationMonitor/SpaceStationMonitor.csproj` | Project file with OTel NuGet packages |
| `src/SpaceStationMonitor/Telemetry.cs` | Static OTel primitives: ActivitySource, Meter, counters, histogram |
| `src/SpaceStationMonitor/Station.cs` | Subsystem model, health, per-subsystem degradation, hull integrity, emergency power |
| `src/SpaceStationMonitor/RepairSystem.cs` | Repair logic with intentional bug (leaky + hard zero) and commented-out fix |
| `src/SpaceStationMonitor/EventEngine.cs` | Random station events (solar flare, micrometeorite, power surge) |
| `src/SpaceStationMonitor/CascadeEngine.cs` | Cascade failure detection when subsystems go critical |
| `src/SpaceStationMonitor/GameDisplay.cs` | Console rendering with health bars, warnings, event messages |
| `src/SpaceStationMonitor/Program.cs` | OTel pipeline setup, gauge registration, main game loop, input handling, telemetry orchestration |
| `tests/SpaceStationMonitor.Tests/SpaceStationMonitor.Tests.csproj` | Test project |
| `tests/SpaceStationMonitor.Tests/StationTests.cs` | Tests for degradation, hull integrity, emergency power |
| `tests/SpaceStationMonitor.Tests/RepairSystemTests.cs` | Tests for normal repair, leaky repair, hard zero |
| `tests/SpaceStationMonitor.Tests/CascadeEngineTests.cs` | Tests for cascade trigger and multiplier behavior |

### Files to modify

| File | Change |
|------|--------|
| `MyFirstOtelProject.sln` | Add SpaceStationMonitor and test projects |

---

### Task 1: Project scaffolding and solution integration

**Files:**
- Create: `src/SpaceStationMonitor/SpaceStationMonitor.csproj`
- Create: `tests/SpaceStationMonitor.Tests/SpaceStationMonitor.Tests.csproj`
- Modify: `MyFirstOtelProject.sln`

- [ ] **Step 1: Create SpaceStationMonitor.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenTelemetry" Version="1.15.1" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.1" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.1" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create test project csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\SpaceStationMonitor\SpaceStationMonitor.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create minimal Program.cs placeholder**

Create `src/SpaceStationMonitor/Program.cs`:

```csharp
// Space Station Monitor — placeholder until full implementation
Console.WriteLine("Space Station Monitor — starting...");
```

- [ ] **Step 4: Add both projects to solution**

```bash
cd C:/dev/MyFirstOtelProject
dotnet sln add src/SpaceStationMonitor/SpaceStationMonitor.csproj
dotnet sln add tests/SpaceStationMonitor.Tests/SpaceStationMonitor.Tests.csproj
```

- [ ] **Step 5: Verify build**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/SpaceStationMonitor/SpaceStationMonitor.csproj src/SpaceStationMonitor/Program.cs tests/SpaceStationMonitor.Tests/SpaceStationMonitor.Tests.csproj MyFirstOtelProject.sln
git commit -m "feat: scaffold SpaceStationMonitor project and test project"
```

---

### Task 2: Telemetry.cs — OTel primitives

**Files:**
- Create: `src/SpaceStationMonitor/Telemetry.cs`

- [ ] **Step 1: Create Telemetry.cs**

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SpaceStationMonitor;

public static class Telemetry
{
    public const string ServiceName = "SpaceStationMonitor";
    public const string ActivitySourceName = "SpaceStationMonitor";
    public const string MeterName = "SpaceStationMonitor";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Counters
    public static readonly Counter<long> RepairsTotal =
        Meter.CreateCounter<long>("station.repairs.total", description: "Total repair attempts");

    public static readonly Counter<long> RepairsFailed =
        Meter.CreateCounter<long>("station.repairs.failed", description: "Hard-zero repair failures");

    public static readonly Counter<long> CascadeFailures =
        Meter.CreateCounter<long>("station.cascade.failures", description: "Cascade failure events");

    public static readonly Counter<long> EventsTotal =
        Meter.CreateCounter<long>("station.events.total", description: "Random station events");

    public static readonly Counter<long> CyclesTotal =
        Meter.CreateCounter<long>("station.cycles.total", description: "Station cycles completed");

    // Histogram
    public static readonly Histogram<double> RepairEffectiveness =
        Meter.CreateHistogram<double>("station.repair.effectiveness", "percent",
            description: "Ratio of repair applied vs requested (100 = healthy)");

    // Note: ObservableGauges (station.subsystem.health, station.hull.integrity)
    // are registered in Program.cs because they need a reference to the Station instance.
}
```

- [ ] **Step 2: Verify build**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build src/SpaceStationMonitor
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/SpaceStationMonitor/Telemetry.cs
git commit -m "feat: add SpaceStationMonitor OTel primitives (counters, histogram)"
```

---

### Task 3: Station.cs — subsystem model and degradation

**Files:**
- Create: `src/SpaceStationMonitor/Station.cs`
- Create: `tests/SpaceStationMonitor.Tests/StationTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/SpaceStationMonitor.Tests/StationTests.cs`:

```csharp
using SpaceStationMonitor;

namespace SpaceStationMonitor.Tests;

public class StationTests
{
    [Fact]
    public void NewStation_AllSubsystemsStartAt100Health()
    {
        var station = new Station();

        foreach (var sub in station.Subsystems)
            Assert.Equal(100.0, sub.Health);
    }

    [Fact]
    public void HullIntegrity_IsAverageOfSubsystemHealth()
    {
        var station = new Station();
        station.Subsystems[0].Health = 80;
        station.Subsystems[1].Health = 60;
        station.Subsystems[2].Health = 40;
        station.Subsystems[3].Health = 20;

        Assert.Equal(50.0, station.HullIntegrity);
    }

    [Fact]
    public void DegradeSubsystem_ReducesHealth()
    {
        var station = new Station();
        station.StartNewCycle();
        var sub = station.Subsystems[0];
        var before = sub.Health;

        // Degrade multiple times to account for random variance
        for (int i = 0; i < 10; i++)
            station.DegradeSubsystem(sub);

        Assert.True(sub.Health < before, "Health should decrease after degradation");
    }

    [Fact]
    public void DegradeSubsystem_HealthNeverBelowZero()
    {
        var station = new Station();
        station.StartNewCycle();
        var sub = station.Subsystems[0];

        for (int i = 0; i < 1000; i++)
            station.DegradeSubsystem(sub);

        Assert.True(sub.Health >= 0, "Health should never go below 0");
    }

    [Fact]
    public void UseEmergencyPower_AddsHealthToAllSubsystems()
    {
        var station = new Station();
        foreach (var sub in station.Subsystems)
            sub.Health = 50;

        var result = station.UseEmergencyPower();

        Assert.True(result);
        foreach (var sub in station.Subsystems)
            Assert.Equal(60, sub.Health);
    }

    [Fact]
    public void UseEmergencyPower_LimitedUses()
    {
        var station = new Station();

        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.True(station.UseEmergencyPower());
        Assert.False(station.UseEmergencyPower()); // 4th attempt fails
    }

    [Fact]
    public void UseEmergencyPower_CapsHealthAt100()
    {
        var station = new Station();
        // All at 100 already
        station.UseEmergencyPower();

        foreach (var sub in station.Subsystems)
            Assert.Equal(100, sub.Health);
    }

    [Fact]
    public void Subsystem_IsCritical_WhenBelow30()
    {
        var sub = new Subsystem("Test", 1.0);
        sub.Health = 29;
        Assert.True(sub.IsCritical);

        sub.Health = 30;
        Assert.False(sub.IsCritical);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests
```

Expected: FAIL — `Station`, `Subsystem` types not found.

- [ ] **Step 3: Implement Station.cs**

Create `src/SpaceStationMonitor/Station.cs`:

```csharp
namespace SpaceStationMonitor;

public class Subsystem
{
    public string Name { get; }
    public double Health { get; set; }
    public double BaseDegradationRate { get; }
    public double CascadeMultiplier { get; set; } = 1.0;

    public Subsystem(string name, double baseDegradationRate)
    {
        Name = name;
        Health = 100.0;
        BaseDegradationRate = baseDegradationRate;
    }

    public bool IsCritical => Health < 30.0;
    public bool IsDown => Health <= 0.0;
}

public class Station
{
    private readonly Random _random = new();
    private double _difficultyMultiplier = 1.0;

    public Subsystem[] Subsystems { get; } =
    [
        new("Oxygen", 2.0),
        new("Power", 1.5),
        new("Shields", 3.0),
        new("Thermal", 1.8)
    ];

    public int EmergencyPowerRemaining { get; private set; } = 3;
    public int CycleCount { get; private set; }
    public DateTime StartTime { get; } = DateTime.UtcNow;

    public double HullIntegrity =>
        Subsystems.Average(s => Math.Max(0, s.Health));

    public void StartNewCycle()
    {
        CycleCount++;
        _difficultyMultiplier = 1.0 + (CycleCount * 0.02);
    }

    public void DegradeSubsystem(Subsystem subsystem)
    {
        var variance = (_random.NextDouble() - 0.3) * 2.0;
        var degradation = subsystem.BaseDegradationRate
            * subsystem.CascadeMultiplier
            * _difficultyMultiplier
            + variance;
        subsystem.Health = Math.Max(0, subsystem.Health - Math.Max(0, degradation));
    }

    public bool UseEmergencyPower()
    {
        if (EmergencyPowerRemaining <= 0)
            return false;

        EmergencyPowerRemaining--;
        foreach (var sub in Subsystems)
            sub.Health = Math.Min(100, sub.Health + 10);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests
```

Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStationMonitor/Station.cs tests/SpaceStationMonitor.Tests/StationTests.cs
git commit -m "feat: add Station model with subsystem degradation and emergency power"
```

---

### Task 4: RepairSystem.cs — repair logic with intentional bug

**Files:**
- Create: `src/SpaceStationMonitor/RepairSystem.cs`
- Create: `tests/SpaceStationMonitor.Tests/RepairSystemTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/SpaceStationMonitor.Tests/RepairSystemTests.cs`:

```csharp
using SpaceStationMonitor;

namespace SpaceStationMonitor.Tests;

public class RepairSystemTests
{
    [Fact]
    public void Repair_WhenBugNotActive_AppliesFullAmount()
    {
        // Bug delay far in future — bug will not be active
        var repair = new RepairSystem("Oxygen", bugActivationDelay: TimeSpan.FromHours(1));
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        Assert.Equal(20, result.Applied);
        Assert.Equal(20, result.Requested);
        Assert.True(result.IsHealthy);
        Assert.Equal(70, sub.Health);
    }

    [Fact]
    public void Repair_WhenBugActive_AppliesReducedAmount()
    {
        // Bug activates immediately
        var repair = new RepairSystem("Oxygen", bugActivationDelay: TimeSpan.Zero);
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        Assert.True(result.Applied < result.Requested,
            $"Expected applied ({result.Applied}) < requested ({result.Requested})");
        Assert.False(result.IsHealthy);
    }

    [Fact]
    public void Repair_WhenBugActive_OnlyAffectsTargetSubsystem()
    {
        var repair = new RepairSystem("Oxygen", bugActivationDelay: TimeSpan.Zero);
        var power = new Subsystem("Power", 1.0);
        power.Health = 50;

        var result = repair.Repair(power, 20);

        Assert.Equal(20, result.Applied);
        Assert.True(result.IsHealthy);
    }

    [Fact]
    public void Repair_DisplayedAfter_ShowsExpectedValue()
    {
        var repair = new RepairSystem("Oxygen", bugActivationDelay: TimeSpan.Zero);
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 50;

        var result = repair.Repair(sub, 20);

        // Display should show what SHOULD have happened (the lie)
        Assert.Equal(70, result.DisplayedAfter);
        // But actual health is less
        Assert.True(result.HealthAfter < 70);
    }

    [Fact]
    public void Repair_CapsHealthAt100()
    {
        var repair = new RepairSystem("Oxygen", bugActivationDelay: TimeSpan.FromHours(1));
        var sub = new Subsystem("Oxygen", 1.0);
        sub.Health = 95;

        var result = repair.Repair(sub, 20);

        Assert.Equal(100, sub.Health);
    }

    [Fact]
    public void GetRepairAmount_ReturnsBetween15And25()
    {
        var repair = new RepairSystem("Oxygen");

        for (int i = 0; i < 100; i++)
        {
            var amount = repair.GetRepairAmount();
            Assert.InRange(amount, 15, 25);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests --filter "FullyQualifiedName~RepairSystem"
```

Expected: FAIL — `RepairSystem` type not found.

- [ ] **Step 3: Implement RepairSystem.cs**

Create `src/SpaceStationMonitor/RepairSystem.cs`:

```csharp
namespace SpaceStationMonitor;

public class RepairSystem
{
    private readonly Random _random = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly TimeSpan _bugActivationDelay;
    private readonly string _bugTargetSubsystem;
    private int _leakyRepairCount;

    public RepairSystem(string bugTargetSubsystem, TimeSpan? bugActivationDelay = null)
    {
        _bugActivationDelay = bugActivationDelay ?? TimeSpan.FromMinutes(3);
        _bugTargetSubsystem = bugTargetSubsystem;
    }

    public string BugTargetSubsystem => _bugTargetSubsystem;
    public bool IsBugActive => DateTime.UtcNow - _startTime > _bugActivationDelay;

    public RepairResult Repair(Subsystem subsystem, int requested)
    {
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ BUG: Repair leak                                               │
        // │ Comment this block and uncomment the FIX block below to fix.   │
        // └─────────────────────────────────────────────────────────────────┘
        int applied;
        if (IsBugActive && subsystem.Name == _bugTargetSubsystem)
        {
            applied = CalculateLeakyRepair(requested);
        }
        else
        {
            applied = requested;
        }

        // ┌─────────────────────────────────────────────────────────────────┐
        // │ FIX: Correct repair logic                                      │
        // │ Uncomment the line below and comment the BUG block above.      │
        // └─────────────────────────────────────────────────────────────────┘
        // int applied = requested;

        double healthBefore = subsystem.Health;
        subsystem.Health = Math.Min(100, subsystem.Health + applied);
        double healthAfter = subsystem.Health;

        // The display shows what SHOULD have happened (the lie)
        double displayedAfter = Math.Min(100, healthBefore + requested);

        return new RepairResult(
            SubsystemName: subsystem.Name,
            Requested: requested,
            Applied: applied,
            HealthBefore: healthBefore,
            HealthAfter: healthAfter,
            DisplayedAfter: displayedAfter,
            IsHealthy: applied == requested
        );
    }

    private int CalculateLeakyRepair(int requested)
    {
        _leakyRepairCount++;

        // 10% chance of hard zero, but only after 2+ leaky repairs
        if (_leakyRepairCount > 2 && _random.NextDouble() < 0.1)
        {
            return 0;
        }

        // Leaky: apply only 20-30% of requested
        double leakFactor = 0.2 + (_random.NextDouble() * 0.1);
        return (int)(requested * leakFactor);
    }

    public int GetRepairAmount() => _random.Next(15, 26);
}

public record RepairResult(
    string SubsystemName,
    int Requested,
    int Applied,
    double HealthBefore,
    double HealthAfter,
    double DisplayedAfter,
    bool IsHealthy
);
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests --filter "FullyQualifiedName~RepairSystem"
```

Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStationMonitor/RepairSystem.cs tests/SpaceStationMonitor.Tests/RepairSystemTests.cs
git commit -m "feat: add RepairSystem with intentional leaky-repair bug and live-fix pattern"
```

---

### Task 5: EventEngine.cs — random station events

**Files:**
- Create: `src/SpaceStationMonitor/EventEngine.cs`

- [ ] **Step 1: Create EventEngine.cs**

```csharp
namespace SpaceStationMonitor;

public enum StationEventType
{
    SolarFlare,
    Micrometeorite,
    PowerSurge
}

public enum EventSeverity
{
    Minor,
    Moderate,
    Severe
}

public record StationEvent(
    StationEventType Type,
    EventSeverity Severity,
    string AffectedSubsystem,
    double HealthImpact
);

public class EventEngine
{
    private readonly Random _random = new();
    private static readonly string[] SubsystemNames = ["Oxygen", "Power", "Shields", "Thermal"];

    public StationEvent? TryGenerateEvent()
    {
        // ~20% chance per cycle
        if (_random.NextDouble() > 0.2)
            return null;

        var type = (StationEventType)_random.Next(3);
        var severity = (EventSeverity)_random.Next(3);

        var targetSubsystem = type switch
        {
            StationEventType.SolarFlare => "Shields",
            StationEventType.PowerSurge => "Power",
            StationEventType.Micrometeorite => SubsystemNames[_random.Next(4)],
            _ => SubsystemNames[_random.Next(4)]
        };

        var impact = severity switch
        {
            EventSeverity.Minor => 5.0 + _random.NextDouble() * 5,
            EventSeverity.Moderate => 10.0 + _random.NextDouble() * 10,
            EventSeverity.Severe => 20.0 + _random.NextDouble() * 15,
            _ => 10.0
        };

        return new StationEvent(type, severity, targetSubsystem, impact);
    }

    public void ApplyEvent(StationEvent stationEvent, Station station)
    {
        var sub = station.Subsystems.FirstOrDefault(s => s.Name == stationEvent.AffectedSubsystem);
        if (sub != null)
        {
            sub.Health = Math.Max(0, sub.Health - stationEvent.HealthImpact);
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build src/SpaceStationMonitor
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/SpaceStationMonitor/EventEngine.cs
git commit -m "feat: add EventEngine for random station events (solar flare, micrometeorite, power surge)"
```

---

### Task 6: CascadeEngine.cs — cascade failure detection

**Files:**
- Create: `src/SpaceStationMonitor/CascadeEngine.cs`
- Create: `tests/SpaceStationMonitor.Tests/CascadeEngineTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/SpaceStationMonitor.Tests/CascadeEngineTests.cs`:

```csharp
using SpaceStationMonitor;

namespace SpaceStationMonitor.Tests;

public class CascadeEngineTests
{
    [Fact]
    public void CheckCascades_NoCriticalSystems_NoCascades()
    {
        var station = new Station();
        var engine = new CascadeEngine();

        var results = engine.CheckAndApplyCascades(station);

        Assert.Empty(results);
        foreach (var sub in station.Subsystems)
            Assert.Equal(1.0, sub.CascadeMultiplier);
    }

    [Fact]
    public void CheckCascades_CriticalSystem_TriggersForOthers()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen goes critical
        var engine = new CascadeEngine();

        var results = engine.CheckAndApplyCascades(station);

        Assert.Single(results);
        Assert.Equal("Oxygen", results[0].SourceSubsystem);
        Assert.True(results[0].Triggered);
        Assert.Equal(3, results[0].AffectedSubsystems.Length);

        // Other subsystems should have increased cascade multiplier
        Assert.Equal(1.0, station.Subsystems[0].CascadeMultiplier); // source not affected by own cascade
        Assert.Equal(1.5, station.Subsystems[1].CascadeMultiplier);
        Assert.Equal(1.5, station.Subsystems[2].CascadeMultiplier);
        Assert.Equal(1.5, station.Subsystems[3].CascadeMultiplier);
    }

    [Fact]
    public void CheckCascades_MultipleCritical_StacksMultipliers()
    {
        var station = new Station();
        station.Subsystems[0].Health = 20; // Oxygen critical
        station.Subsystems[2].Health = 15; // Shields critical
        var engine = new CascadeEngine();

        var results = engine.CheckAndApplyCascades(station);

        Assert.Equal(2, results.Count);

        // Power and Thermal affected by both cascades: 1.0 + 0.5 + 0.5 = 2.0
        Assert.Equal(1.5, station.Subsystems[0].CascadeMultiplier); // Oxygen: from Shields cascade only
        Assert.Equal(2.0, station.Subsystems[1].CascadeMultiplier); // Power: from both
        Assert.Equal(1.5, station.Subsystems[2].CascadeMultiplier); // Shields: from Oxygen cascade only
        Assert.Equal(2.0, station.Subsystems[3].CascadeMultiplier); // Thermal: from both
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests --filter "FullyQualifiedName~CascadeEngine"
```

Expected: FAIL — `CascadeEngine` type not found.

- [ ] **Step 3: Implement CascadeEngine.cs**

Create `src/SpaceStationMonitor/CascadeEngine.cs`:

```csharp
namespace SpaceStationMonitor;

public record CascadeResult(
    string SourceSubsystem,
    string[] AffectedSubsystems,
    bool Triggered
);

public class CascadeEngine
{
    public List<CascadeResult> CheckAndApplyCascades(Station station)
    {
        var results = new List<CascadeResult>();

        // Reset all cascade multipliers
        foreach (var sub in station.Subsystems)
            sub.CascadeMultiplier = 1.0;

        foreach (var sub in station.Subsystems)
        {
            if (!sub.IsCritical)
                continue;

            var affected = station.Subsystems
                .Where(s => s.Name != sub.Name)
                .ToArray();

            foreach (var target in affected)
                target.CascadeMultiplier += 0.5;

            results.Add(new CascadeResult(
                SourceSubsystem: sub.Name,
                AffectedSubsystems: affected.Select(a => a.Name).ToArray(),
                Triggered: true
            ));
        }

        return results;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests --filter "FullyQualifiedName~CascadeEngine"
```

Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/SpaceStationMonitor/CascadeEngine.cs tests/SpaceStationMonitor.Tests/CascadeEngineTests.cs
git commit -m "feat: add CascadeEngine for cascade failure detection and propagation"
```

---

### Task 7: GameDisplay.cs — console rendering

**Files:**
- Create: `src/SpaceStationMonitor/GameDisplay.cs`

- [ ] **Step 1: Create GameDisplay.cs**

```csharp
namespace SpaceStationMonitor;

public class GameDisplay
{
    private string? _currentEvent;
    private string? _currentWarning;
    private string? _lastRepairMessage;

    public void SetEvent(string? message) => _currentEvent = message;
    public void SetWarning(string? message) => _currentWarning = message;
    public void SetRepairMessage(string? message) => _lastRepairMessage = message;

    public void Render(Station station)
    {
        Console.Clear();
        var uptime = DateTime.UtcNow - station.StartTime;
        var hullStr = $"{station.HullIntegrity:F0}%";
        var uptimeStr = $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║          SPACE STATION MONITOR v1.0              ║");

        Console.ForegroundColor = station.HullIntegrity < 30 ? ConsoleColor.Red : ConsoleColor.Cyan;
        Console.WriteLine($"║          Hull Integrity: {hullStr,-25}║");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        for (int i = 0; i < station.Subsystems.Length; i++)
        {
            var sub = station.Subsystems[i];
            var barLength = (int)(sub.Health / 100.0 * 16);
            var bar = new string('\u2588', barLength) + new string('\u2591', 16 - barLength);
            var warning = sub.IsCritical ? " \u26A0" : "  ";
            var healthStr = $"{sub.Health:F0}%";

            Console.ForegroundColor = sub.Health switch
            {
                < 15 => ConsoleColor.Red,
                < 30 => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            Console.WriteLine($"║  [{i + 1}] {sub.Name,-16} {bar}  {healthStr,-4}{warning}  ║");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");

        var hasMessage = false;
        if (_currentWarning != null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WritePaddedLine($"  \u26A0 {_currentWarning}");
            hasMessage = true;
        }
        if (_currentEvent != null)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            WritePaddedLine($"  \u2604 {_currentEvent}");
            hasMessage = true;
        }
        if (_lastRepairMessage != null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            WritePaddedLine($"  > {_lastRepairMessage}");
            hasMessage = true;
        }
        if (!hasMessage)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            WritePaddedLine("  All systems nominal.");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╠══════════════════════════════════════════════════╣");
        Console.WriteLine("║  [1-4] Select   [R] Repair   [E] Emergency Pwr  ║");
        Console.WriteLine($"║  [Q] Quit       Emergency Power: {station.EmergencyPowerRemaining} left          ║");
        Console.WriteLine($"║  Cycle: {station.CycleCount,-7}|  Uptime: {uptimeStr,-19}║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void WritePaddedLine(string content)
    {
        // Box is 50 chars wide including borders
        const int boxWidth = 50;
        var inner = content.Length > boxWidth - 4 ? content[..(boxWidth - 4)] : content;
        var padding = boxWidth - 2 - inner.Length; // 2 for ║ borders
        Console.WriteLine($"║{inner}{new string(' ', Math.Max(0, padding))}║");
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build src/SpaceStationMonitor
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/SpaceStationMonitor/GameDisplay.cs
git commit -m "feat: add GameDisplay with health bars, warnings, and event rendering"
```

---

### Task 8: Program.cs — OTel pipeline, main loop, and telemetry orchestration

**Files:**
- Modify: `src/SpaceStationMonitor/Program.cs`

This is the central file that wires everything together: OTel setup, gauge registration, game loop, input handling, and all span/metric/log instrumentation.

- [ ] **Step 1: Replace placeholder Program.cs with full implementation**

Replace `src/SpaceStationMonitor/Program.cs` with:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpaceStationMonitor;

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

var logger = loggerFactory.CreateLogger("SpaceStationMonitor");

// ── Game components ─────────────────────────────────────────────────────────
var station = new Station();
var random = new Random();
var bugTarget = station.Subsystems[random.Next(station.Subsystems.Length)].Name;
var repairSystem = new RepairSystem(bugTarget);
var eventEngine = new EventEngine();
var cascadeEngine = new CascadeEngine();
var display = new GameDisplay();
var shutdownCts = new CancellationTokenSource();
int selectedSubsystem = 0;

// ── Register gauge metrics (need Station reference) ─────────────────────────
Telemetry.Meter.CreateObservableGauge("station.subsystem.health",
    () => station.Subsystems.Select(s =>
        new Measurement<double>(s.Health,
            new KeyValuePair<string, object?>("subsystem.name", s.Name))),
    "percent", "Current health of each subsystem");

Telemetry.Meter.CreateObservableGauge("station.hull.integrity",
    () => [new Measurement<double>(station.HullIntegrity)],
    "percent", "Overall station hull integrity");

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

logger.LogInformation("Space Station Monitor started. Bug target: {Target} (activates after ~3 min)",
    repairSystem.BugTargetSubsystem);

// ── Main loop ───────────────────────────────────────────────────────────────
try
{
    while (!shutdownCts.IsCancellationRequested && station.HullIntegrity > 0)
    {
        // ── Start cycle span ──
        using var cycleActivity = Telemetry.ActivitySource.StartActivity("StationCycle");
        cycleActivity?.SetTag("cycle.number", station.CycleCount + 1);

        station.StartNewCycle();

        // ── Subsystem degradation (one child span per subsystem) ──
        foreach (var sub in station.Subsystems)
        {
            using var tickActivity = Telemetry.ActivitySource.StartActivity("SubsystemTick");
            var healthBefore = sub.Health;

            station.DegradeSubsystem(sub);

            tickActivity?.SetTag("subsystem.name", sub.Name);
            tickActivity?.SetTag("health.before", Math.Round(healthBefore, 1));
            tickActivity?.SetTag("health.after", Math.Round(sub.Health, 1));
            tickActivity?.SetTag("degradation.rate",
                Math.Round(sub.BaseDegradationRate * sub.CascadeMultiplier, 2));

            logger.LogInformation("Subsystem {Name}: {Before:F1}% \u2192 {After:F1}%",
                sub.Name, healthBefore, sub.Health);
        }

        // ── Cascade check ──
        var cascades = cascadeEngine.CheckAndApplyCascades(station);

        var criticalSystems = station.Subsystems.Where(s => s.IsCritical).Select(s => s.Name).ToArray();
        display.SetWarning(criticalSystems.Length > 0
            ? $"WARNING: {string.Join(", ", criticalSystems)} below critical!"
            : null);

        foreach (var cascade in cascades)
        {
            using var cascadeActivity = Telemetry.ActivitySource.StartActivity("CascadeCheck");
            cascadeActivity?.SetTag("cascade.triggered", true);
            cascadeActivity?.SetTag("source.subsystem", cascade.SourceSubsystem);
            cascadeActivity?.SetTag("affected.subsystems",
                string.Join(",", cascade.AffectedSubsystems));

            Telemetry.CascadeFailures.Add(1,
                new KeyValuePair<string, object?>("source.subsystem", cascade.SourceSubsystem),
                new KeyValuePair<string, object?>("affected.subsystem",
                    string.Join(",", cascade.AffectedSubsystems)));

            logger.LogError("Cascade failure: {Source} \u2192 {Affected}",
                cascade.SourceSubsystem, string.Join(", ", cascade.AffectedSubsystems));
        }

        // ── Random events ──
        var stationEvent = eventEngine.TryGenerateEvent();
        if (stationEvent != null)
        {
            using var eventActivity = Telemetry.ActivitySource.StartActivity("StationEvent");
            eventActivity?.SetTag("event.type", stationEvent.Type.ToString());
            eventActivity?.SetTag("event.severity", stationEvent.Severity.ToString());
            eventActivity?.SetTag("subsystem.affected", stationEvent.AffectedSubsystem);

            eventEngine.ApplyEvent(stationEvent, station);

            Telemetry.EventsTotal.Add(1,
                new KeyValuePair<string, object?>("event.type", stationEvent.Type.ToString()),
                new KeyValuePair<string, object?>("event.severity", stationEvent.Severity.ToString()));

            display.SetEvent(
                $"EVENT: {stationEvent.Type} ({stationEvent.Severity}) hit {stationEvent.AffectedSubsystem}!");

            logger.LogInformation("Station event: {Type} (severity {Severity}) hit {Subsystem}",
                stationEvent.Type, stationEvent.Severity, stationEvent.AffectedSubsystem);
        }
        else
        {
            display.SetEvent(null);
        }

        Telemetry.CyclesTotal.Add(1);
        cycleActivity?.SetTag("hull.integrity", Math.Round(station.HullIntegrity, 1));

        logger.LogInformation("Station cycle {Cycle} complete \u2014 hull integrity {Hull:F1}%",
            station.CycleCount, station.HullIntegrity);

        // ── Render display ──
        display.Render(station);

        // ── Wait for input or next cycle ──
        var cycleInterval = TimeSpan.FromSeconds(random.Next(5, 11));
        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);
        cycleCts.CancelAfter(cycleInterval);

        try
        {
            while (!cycleCts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    switch (char.ToUpperInvariant(key.KeyChar))
                    {
                        case '1': selectedSubsystem = 0; display.Render(station); break;
                        case '2': selectedSubsystem = 1; display.Render(station); break;
                        case '3': selectedSubsystem = 2; display.Render(station); break;
                        case '4': selectedSubsystem = 3; display.Render(station); break;

                        case 'R':
                            HandleRepair(station, repairSystem, selectedSubsystem, display, logger);
                            break;

                        case 'E':
                            HandleEmergencyPower(station, display, logger);
                            break;

                        case 'Q':
                            shutdownCts.Cancel();
                            break;
                    }
                }
                await Task.Delay(100, cycleCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Cycle timer expired or shutdown
        }

        display.SetRepairMessage(null);
    }
}
catch (OperationCanceledException)
{
    // Normal shutdown via Ctrl+C
}

// ── Game over ───────────────────────────────────────────────────────────────
Console.Clear();
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
Console.ResetColor();

logger.LogInformation("Game over \u2014 Cycles: {Cycles}, Hull: {Hull:F1}%",
    station.CycleCount, station.HullIntegrity);

// ── Cleanup ─────────────────────────────────────────────────────────────────
meterProvider?.Dispose();
tracerProvider?.Dispose();
loggerFactory?.Dispose();

// ── Helper methods ──────────────────────────────────────────────────────────

void HandleRepair(Station station, RepairSystem repairSystem, int subsystemIndex,
    GameDisplay display, ILogger logger)
{
    var sub = station.Subsystems[subsystemIndex];
    var requested = repairSystem.GetRepairAmount();

    using var repairActivity = Telemetry.ActivitySource.StartActivity("RepairAction");
    var result = repairSystem.Repair(sub, requested);

    repairActivity?.SetTag("subsystem.name", result.SubsystemName);
    repairActivity?.SetTag("repair.requested", result.Requested);
    repairActivity?.SetTag("repair.applied", result.Applied);
    repairActivity?.SetTag("repair.healthy", result.IsHealthy);

    Telemetry.RepairsTotal.Add(1,
        new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

    double effectiveness = result.Requested > 0
        ? (double)result.Applied / result.Requested * 100.0
        : 0;
    Telemetry.RepairEffectiveness.Record(effectiveness,
        new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

    if (!result.IsHealthy)
    {
        if (result.Applied == 0)
        {
            // Hard zero — record exception on the span
            var ex = new InvalidOperationException(
                $"Repair failed on {result.SubsystemName}: requested {result.Requested}% applied 0%");
            repairActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            repairActivity?.RecordException(ex);
            repairActivity?.AddEvent(new ActivityEvent("RepairFailed"));

            Telemetry.RepairsFailed.Add(1,
                new KeyValuePair<string, object?>("subsystem.name", result.SubsystemName));

            logger.LogError("Repair failed on {Name}: requested {Requested}% but applied 0%",
                result.SubsystemName, result.Requested);
        }
        else
        {
            // Leaky repair — record span event with delta
            repairActivity?.AddEvent(new ActivityEvent("RepairLeak",
                tags: new ActivityTagsCollection
                {
                    { "repair.delta", result.Requested - result.Applied }
                }));

            logger.LogError("Repair leak on {Name}: requested {Requested}% applied {Applied}%",
                result.SubsystemName, result.Requested, result.Applied);
        }
    }
    else
    {
        logger.LogInformation("Repair applied to {Name}: {Before:F1}% \u2192 {After:F1}%",
            result.SubsystemName, result.HealthBefore, result.HealthAfter);
    }

    // Display shows the lie (full expected values, not actual)
    display.SetRepairMessage(
        $"Repaired {result.SubsystemName}: {result.HealthBefore:F0}% \u2192 {result.DisplayedAfter:F0}%");
    display.Render(station);
}

void HandleEmergencyPower(Station station, GameDisplay display, ILogger logger)
{
    if (station.UseEmergencyPower())
    {
        logger.LogInformation("Emergency power used. Remaining: {Remaining}",
            station.EmergencyPowerRemaining);
        display.SetRepairMessage(
            $"Emergency power! All systems +10%. ({station.EmergencyPowerRemaining} left)");
    }
    else
    {
        display.SetRepairMessage("No emergency power remaining!");
    }
    display.Render(station);
}
```

- [ ] **Step 2: Verify full build (all projects)**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Run all tests**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test tests/SpaceStationMonitor.Tests
```

Expected: All 17 tests pass (8 Station + 6 RepairSystem + 3 CascadeEngine).

- [ ] **Step 4: Commit**

```bash
git add src/SpaceStationMonitor/Program.cs
git commit -m "feat: implement SpaceStationMonitor main loop with OTel traces, metrics, logs, and input handling"
```

---

### Task 9: End-to-end verification

- [ ] **Step 1: Build the full solution**

```bash
cd C:/dev/MyFirstOtelProject
dotnet build
```

Expected: Build succeeded for all 3 projects (ComplimentGenerator, OTelWizard, SpaceStationMonitor).

- [ ] **Step 2: Run all tests**

```bash
cd C:/dev/MyFirstOtelProject
dotnet test
```

Expected: All tests pass.

- [ ] **Step 3: Smoke test — run SpaceStationMonitor briefly**

Start OTelWizard in the background, then SpaceStationMonitor:

```bash
cd C:/dev/MyFirstOtelProject/src/OTelWizard
dotnet run &
sleep 3
cd C:/dev/MyFirstOtelProject/src/SpaceStationMonitor
timeout 15 dotnet run || true
```

Expected: SpaceStationMonitor renders the station display. OTelWizard receives and explains traces, metrics, and logs with color-coded output.

- [ ] **Step 4: Final commit if any adjustments were needed**

```bash
cd C:/dev/MyFirstOtelProject
git add -A
git status
# Only commit if there are changes
git diff --cached --quiet || git commit -m "fix: adjustments from end-to-end verification"
```
