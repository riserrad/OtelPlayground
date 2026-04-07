using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;

namespace OTelWizard;

public static class TelemetryExplainer
{
    public static void ExplainTraces(ExportTraceServiceRequest request)
    {
        foreach (var resourceSpans in request.ResourceSpans)
        {
            var serviceName = resourceSpans.Resource?.Attributes
                .FirstOrDefault(a => a.Key == "service.name")?.Value?.StringValue ?? "unknown";

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                foreach (var span in scopeSpans.Spans)
                {
                    var traceId = Convert.ToHexString(span.TraceId.ToByteArray()).ToLowerInvariant();
                    var spanId = Convert.ToHexString(span.SpanId.ToByteArray()).ToLowerInvariant();
                    var name = span.Name;
                    var durationMs = (span.EndTimeUnixNano - span.StartTimeUnixNano) / 1_000_000.0;

                    var attributes = string.Join(", ",
                        span.Attributes.Select(a => $"{a.Key}={FormatValue(a.Value)}"));

                    ConsoleRenderer.WriteTrace($"Span: \"{name}\" | TraceID: {traceId[..16]}... | SpanID: {spanId[..16]}");

                    if (!string.IsNullOrEmpty(attributes))
                    {
                        ConsoleRenderer.WriteTrace($"  Attributes: {attributes}");
                    }

                    if (durationMs > 0)
                    {
                        ConsoleRenderer.WriteTrace($"  Duration: {durationMs:F1}ms");
                    }

                    ConsoleRenderer.WriteWizard(
                        $"A Span represents a unit of work in your app. This span \"{name}\" belongs to " +
                        $"service \"{serviceName}\". The TraceID groups related spans into a single trace. " +
                        $"In Jaeger or Aspire Dashboard, you'd search: service.name=\"{serviceName}\" to find this.");

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }

    public static void ExplainMetrics(ExportMetricsServiceRequest request)
    {
        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                foreach (var metric in scopeMetrics.Metrics)
                {
                    var name = metric.Name;
                    var description = metric.Description;

                    switch (metric.DataCase)
                    {
                        case Metric.DataOneofCase.Sum:
                            var sum = metric.Sum;
                            foreach (var dp in sum.DataPoints)
                            {
                                var value = dp.AsInt != 0 ? dp.AsInt : dp.AsDouble;
                                ConsoleRenderer.WriteMetric($"Counter: \"{name}\" = {value} | {description}");
                                ConsoleRenderer.WriteWizard(
                                    $"This is a Counter metric — it only goes up. \"{name}\" tracks a cumulative total. " +
                                    $"Counters are perfect for things you want to count over time (requests, errors, compliments). " +
                                    $"In PromQL you'd query: rate({name.Replace(".", "_")}[5m]) to see the per-second rate.");
                            }
                            break;

                        case Metric.DataOneofCase.Histogram:
                            var histogram = metric.Histogram;
                            foreach (var dp in histogram.DataPoints)
                            {
                                ConsoleRenderer.WriteMetric(
                                    $"Histogram: \"{name}\" | Count: {dp.Count}, Sum: {dp.Sum:F2} | {description}");
                                ConsoleRenderer.WriteWizard(
                                    $"A Histogram records the distribution of values (like response times). " +
                                    $"\"{name}\" has recorded {dp.Count} observations with a sum of {dp.Sum:F2}. " +
                                    $"Histograms let you compute percentiles (p50, p99). " +
                                    $"In PromQL: histogram_quantile(0.95, rate({name.Replace(".", "_")}_bucket[5m]))");
                            }
                            break;

                        default:
                            ConsoleRenderer.WriteMetric($"{metric.DataCase}: \"{name}\" | {description}");
                            break;
                    }

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }

    public static void ExplainLogs(ExportLogsServiceRequest request)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var log in scopeLogs.LogRecords)
                {
                    var severity = log.SeverityText;
                    var body = log.Body?.StringValue ?? "(empty)";
                    var traceId = log.TraceId.IsEmpty
                        ? null
                        : Convert.ToHexString(log.TraceId.ToByteArray()).ToLowerInvariant();

                    var attributes = string.Join(", ",
                        log.Attributes.Select(a => $"{a.Key}={FormatValue(a.Value)}"));

                    ConsoleRenderer.WriteLog($"[{severity}] {body}");

                    if (!string.IsNullOrEmpty(attributes))
                    {
                        ConsoleRenderer.WriteLog($"  Attributes: {attributes}");
                    }

                    if (traceId != null)
                    {
                        ConsoleRenderer.WriteLog($"  TraceID: {traceId[..16]}...");
                    }

                    var traceCorrelation = traceId != null
                        ? " This log carries a TraceID, meaning you can jump from this log directly to the trace that produced it — this is called trace-log correlation."
                        : " This log has no TraceID — it was emitted outside of an active span.";

                    ConsoleRenderer.WriteWizard(
                        $"OTel structured logs carry metadata (severity, attributes, trace context) alongside the message." +
                        traceCorrelation +
                        $" In a log backend, you'd filter: severity=\"{severity}\" AND resource.service.name=\"ComplimentGenerator\"");

                    ConsoleRenderer.WriteSeparator();
                }
            }
        }
    }

    private static string FormatValue(OpenTelemetry.Proto.Common.V1.AnyValue? value)
    {
        if (value == null) return "?";
        return value.ValueCase switch
        {
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue => value.StringValue,
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(),
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString("F2"),
            OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue => value.BoolValue.ToString(),
            _ => value.ToString() ?? "?"
        };
    }
}
