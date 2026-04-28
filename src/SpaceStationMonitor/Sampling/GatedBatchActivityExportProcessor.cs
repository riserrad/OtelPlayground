using System.Diagnostics;
using OpenTelemetry;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// A <see cref="BatchActivityExportProcessor"/> subclass whose direct <see cref="OnEnd"/> is a
/// no-op. Activities reach the underlying batch queue only via <see cref="ForwardEnd"/>, which
/// the tail processor's <c>_next</c> adapter calls for kept activities. The processor is still
/// registered top-level on the tracer builder so the SDK propagates parent-provider state
/// (and the resource it carries) to the wrapped exporter.
/// </summary>
internal sealed class GatedBatchActivityExportProcessor : BatchActivityExportProcessor
{
    public GatedBatchActivityExportProcessor(BaseExporter<Activity> exporter)
        : base(exporter)
    {
    }

    public override void OnEnd(Activity data)
    {
        // Direct SDK delivery is suppressed; only ForwardEnd drives the batch queue.
    }

    public void ForwardEnd(Activity data) => base.OnEnd(data);
}
