using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace OTelWizard.Services;

public class LogsServiceImpl : LogsService.LogsServiceBase
{
    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request, ServerCallContext context)
    {
        TelemetryExplainer.ExplainLogs(request);
        return Task.FromResult(new ExportLogsServiceResponse());
    }
}
