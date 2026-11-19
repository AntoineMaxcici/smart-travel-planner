namespace TravelPlanner.Api.Tests;

// The "fails at startup" tests start a host that throws on purpose. Under heavy parallel load,
// WebApplicationFactory sometimes reports that as ObjectDisposedException instead of the real error,
// so these classes run on their own after the parallel tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StartupFailureCollection
{
    public const string Name = "Startup failure tests";
}
