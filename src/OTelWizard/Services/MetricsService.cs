using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace OTelWizard.Services;

public class MetricsServiceImpl : MetricsService.MetricsServiceBase
{
    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainMetrics(request);
        return Task.FromResult(new ExportMetricsServiceResponse());
    }
}
