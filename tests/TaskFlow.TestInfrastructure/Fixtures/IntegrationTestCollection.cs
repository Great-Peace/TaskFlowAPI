namespace TaskFlow.TestInfrastructure.Fixtures;

/// <summary>
/// The name of the xUnit collection that every integration test belongs to.
/// </summary>
/// <remarks>
/// <para>
/// xUnit resolves <c>[CollectionDefinition]</c> only within the assembly that
/// contains the tests, so the definition itself cannot live here. Each integration
/// test assembly declares its own one-line definition using this name and
/// <see cref="IntegrationTestFixture"/>; the name is shared from here so the two
/// cannot drift apart.
/// </para>
/// <para>
/// <b>Why these tests run serially.</b> All integration tests share one database.
/// Isolation comes from deleting every row between tests, which is only correct if
/// no other test is mid-flight. Running them in parallel would let one test's reset
/// erase another test's arrangement, producing exactly the kind of intermittent
/// failure that erodes trust in a suite.
/// </para>
/// <para>
/// Membership of a single collection already serialises these tests with respect to
/// each other; the definition additionally sets <c>DisableParallelization</c> so the
/// collection cannot run alongside another collection in the same assembly.
/// </para>
/// <para>
/// <b>The alternative, and why it was not taken.</b> Each test could be given its own
/// database on the shared container, which would permit parallelism. That means
/// creating a schema per test - a real cost per test - and it complicates both the
/// connection-string plumbing and failure diagnosis. It is worth revisiting if the
/// suite grows large enough for the serial run time to hurt; at the current size it
/// would buy little and cost clarity.
/// </para>
/// <para>
/// Unit and architecture tests share no state and run fully parallel.
/// </para>
/// </remarks>
public static class IntegrationTestCollection
{
    /// <summary>The collection name referenced by <c>[Collection]</c> and <c>[CollectionDefinition]</c>.</summary>
    public const string Name = "TaskFlow integration tests";
}
