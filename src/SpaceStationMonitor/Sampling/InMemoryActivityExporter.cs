using System.Diagnostics;
using OpenTelemetry;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// Test-only exporter that captures every <see cref="Activity"/> reaching the export queue
/// into an in-memory list. Activated via <c>OTEL_TEST_CAPTURE=1</c> so headless smoke runs
/// can prove tail-sampling gating empirically: under <c>SAMPLER_PROFILE=tail</c> the captured
/// count must be strictly less than total activities created (and every captured activity
/// must satisfy a keep-criterion).
/// </summary>
public sealed class InMemoryActivityExporter : BaseExporter<Activity>
{
    private readonly List<Activity> _captured = new();
    private readonly object _lock = new();

    public IReadOnlyList<Activity> Captured
    {
        get
        {
            lock (_lock) return _captured.ToArray();
        }
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        lock (_lock)
        {
            foreach (var activity in batch)
                _captured.Add(activity);
        }
        return ExportResult.Success;
    }
}
