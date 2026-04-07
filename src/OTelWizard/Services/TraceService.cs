using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace OTelWizard.Services;

public class TraceServiceImpl : TraceService.TraceServiceBase
{
    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainTraces(request);
        return Task.FromResult(new ExportTraceServiceResponse());
    }
}
