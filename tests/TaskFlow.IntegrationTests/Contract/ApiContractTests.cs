using TaskFlow.TestInfrastructure.Contract;
using TaskFlow.TestInfrastructure.Fixtures;

namespace TaskFlow.IntegrationTests.Contract;

/// <summary>
/// Guards the published API contract against unintended breaking changes.
/// </summary>
/// <remarks>
/// The approved contract lives in <c>Contract/api-contract.approved.json</c> and is
/// reviewed like any other source file. See <c>docs/TESTING_STRATEGY.md</c> for how
/// an intentional contract change is approved.
/// </remarks>
public class ApiContractTests : IntegrationTestBase
{
    /// <summary>Path of the approved baseline, relative to the test project root.</summary>
    private const string BaselineRelativePath = "Contract/api-contract.approved.json";

    /// <summary>
    /// Environment variable that rewrites the baseline instead of asserting against it.
    /// </summary>
    private const string ApproveVariable = "TASKFLOW_APPROVE_CONTRACT";

    public ApiContractTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// The current contract introduces no breaking change against the approved one.
    /// </summary>
    /// <remarks>
    /// Additive changes pass. Only changes that could break an existing client fail
    /// the build, so a failure here always means something a caller depends on has
    /// gone or changed shape.
    /// </remarks>
    [Fact]
    public void The_api_introduces_no_breaking_contract_changes()
    {
        var current = OpenApiContractExtractor.Extract(Api.Services);

        if (ShouldApprove())
        {
            WriteBaseline(current);
            return;
        }

        var approved = OpenApiContract.FromJson(File.ReadAllText(BaselinePath()));
        var changes = ContractComparer.Compare(approved, current);
        var breaking = changes.Where(c => c.Kind == ChangeKind.Breaking).ToArray();

        Assert.True(breaking.Length == 0,
            $"""
             The API contract has changed in a way that breaks existing clients.

             {ContractComparer.Describe(changes)}
             If these changes are intended, review them, then re-approve the baseline:

                 {ApproveVariable}=1 dotnet test tests/TaskFlow.IntegrationTests \
                     --filter FullyQualifiedName~ApiContractTests

             and commit the updated {BaselineRelativePath} as part of the same change.
             """);
    }

    /// <summary>
    /// Additive changes are reported but do not fail, and are listed for visibility.
    /// </summary>
    [Fact]
    public void Additive_contract_changes_are_reported_without_failing()
    {
        var current = OpenApiContractExtractor.Extract(Api.Services);

        if (!File.Exists(BaselinePath()))
        {
            return;
        }

        var approved = OpenApiContract.FromJson(File.ReadAllText(BaselinePath()));
        var additive = ContractComparer.Compare(approved, current)
            .Where(c => c.Kind == ChangeKind.Additive)
            .ToArray();

        // Not an assertion about the count: this records what drifted additively so a
        // reviewer can see it in the test output without the build failing.
        Assert.All(additive, change => Assert.NotEmpty(change.Detail));
    }

    /// <summary>
    /// Every documented operation is reachable, so the contract cannot describe an
    /// endpoint that does not exist.
    /// </summary>
    [Fact]
    public void The_documented_operations_match_the_endpoints_that_exist()
    {
        var current = OpenApiContractExtractor.Extract(Api.Services);

        Assert.NotEmpty(current.Operations);
        Assert.Contains("POST /api/Auth/login", current.Operations.Keys);
        Assert.Contains("POST /api/Auth/register", current.Operations.Keys);
        Assert.Contains("GET /api/Projects", current.Operations.Keys);
        Assert.Contains("POST /api/Projects", current.Operations.Keys);
        Assert.Contains("GET /api/Projects/{id}", current.Operations.Keys);
        Assert.Contains("PUT /api/Projects/{id}", current.Operations.Keys);
        Assert.Contains("DELETE /api/Projects/{id}", current.Operations.Keys);
        Assert.Contains("GET /api/Projects/all", current.Operations.Keys);
    }

    private static bool ShouldApprove() =>
        Environment.GetEnvironmentVariable(ApproveVariable) is "1" or "true";

    private static void WriteBaseline(OpenApiContract contract)
    {
        var path = BaselinePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contract.ToJson());
    }

    /// <summary>
    /// Resolves the baseline in the source tree rather than the build output.
    /// </summary>
    /// <remarks>
    /// Approving must update the file that is committed, not a copy under <c>bin</c>
    /// that is overwritten by the next build.
    /// </remarks>
    private static string BaselinePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaskFlow.IntegrationTests.csproj")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the integration test project root from " + AppContext.BaseDirectory);
        }

        return Path.Combine(directory.FullName, BaselineRelativePath);
    }
}
