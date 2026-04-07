using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ComplimentGenerator;

public static class Telemetry
{
    public const string ServiceName = "ComplimentGenerator";
    public const string ActivitySourceName = "ComplimentGenerator";
    public const string MeterName = "ComplimentGenerator";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // Counters
    public static readonly Counter<long> ComplimentsGenerated =
        Meter.CreateCounter<long>("compliments.generated", description: "Total compliments shown");

    public static readonly Counter<long> ComplimentsLiked =
        Meter.CreateCounter<long>("compliments.liked", description: "Total compliments liked");

    public static readonly Counter<long> ComplimentsDisliked =
        Meter.CreateCounter<long>("compliments.disliked", description: "Total compliments disliked");

    public static readonly Counter<long> ComplimentsSkipped =
        Meter.CreateCounter<long>("compliments.skipped", description: "Total compliments skipped");

    // Histogram
    public static readonly Histogram<double> ResponseTime =
        Meter.CreateHistogram<double>("compliments.response_time", "s",
            description: "Time between compliment shown and feedback received");
}
