using Xunit;

namespace SpaceStationMonitor.Tests;

// .NET ActivitySource sampling is OR'd across all registered listeners (any-says-yes
// wins), so a parallel test's AllData listener would invalidate another test's None
// decision. Serialising the listener-bound tests is the only reliable fix without
// exposing internals.
[CollectionDefinition("ActivityListener-bound", DisableParallelization = true)]
public class ActivityListenerCollection
{
}
