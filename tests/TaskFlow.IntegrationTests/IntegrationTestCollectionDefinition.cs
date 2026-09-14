using TaskFlow.TestInfrastructure.Fixtures;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Binds this assembly's integration tests to the shared SQL Server container and
/// application host.
/// </summary>
/// <remarks>
/// xUnit only discovers collection definitions declared in the test assembly itself,
/// so this one-line definition is the required local counterpart to the reusable
/// <see cref="IntegrationTestFixture"/>. See
/// <see cref="IntegrationTestCollection"/> for why the collection runs serially.
/// </remarks>
[CollectionDefinition(IntegrationTestCollection.Name, DisableParallelization = true)]
public sealed class IntegrationTestCollectionDefinition : ICollectionFixture<IntegrationTestFixture>
{
}
