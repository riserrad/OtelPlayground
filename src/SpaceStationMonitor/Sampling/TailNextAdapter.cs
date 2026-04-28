using System.Diagnostics;
using OpenTelemetry;

namespace SpaceStationMonitor.Sampling;

/// <summary>
/// The <c>_next</c> processor for <see cref="TailSamplingProcessor"/> when an export pipeline is
/// wired in. Forwards each kept activity to the gated batch processor's
/// <see cref="GatedBatchActivityExportProcessor.ForwardEnd"/>, which is the only path that
/// reaches the export queue.
/// </summary>
internal sealed class TailNextAdapter : BaseProcessor<Activity>
{
    private readonly GatedBatchActivityExportProcessor _gated;

    public TailNextAdapter(GatedBatchActivityExportProcessor gated)
    {
        _gated = gated;
    }

    public override void OnEnd(Activity data) => _gated.ForwardEnd(data);
}
