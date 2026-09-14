using TaskFlow.TestInfrastructure.Contract;

namespace TaskFlow.UnitTests.Contract;

/// <summary>
/// The classification rules of the contract comparer.
/// </summary>
/// <remarks>
/// The comparer is the thing standing between a breaking change and production, so
/// each rule is tested in both directions: that it fires when it should, and that the
/// corresponding safe change does not fire it. A checker that silently classifies
/// everything as additive would pass every build while protecting nothing.
/// </remarks>
public class ContractComparerTests
{
    private static OpenApiContract ContractWith(params (string Key, OperationContract Operation)[] operations)
    {
        var contract = new OpenApiContract();
        foreach (var (key, operation) in operations)
        {
            contract.Operations[key] = operation;
        }

        return contract;
    }

    private static OperationContract Operation(
        string[]? responses = null,
        (string Name, bool Required)[]? parameters = null,
        string? requestBody = null,
        bool requiresAuthentication = false,
        string[]? roles = null) => new()
        {
            Responses = new SortedSet<string>(responses ?? new[] { "200" }),
            Parameters = new SortedDictionary<string, bool>(
                (parameters ?? Array.Empty<(string, bool)>()).ToDictionary(p => p.Name, p => p.Required)),
            RequestBodySchema = requestBody,
            RequiresAuthentication = requiresAuthentication,
            RequiredRoles = new SortedSet<string>(roles ?? Array.Empty<string>())
        };

    private static OpenApiContract SchemaContractWith(
        string name,
        (string Property, string Type)[] properties,
        string[]? required = null)
    {
        var contract = new OpenApiContract();
        contract.Schemas[name] = new SchemaContract
        {
            Properties = new SortedDictionary<string, string>(
                properties.ToDictionary(p => p.Property, p => p.Type)),
            Required = new SortedSet<string>(required ?? Array.Empty<string>())
        };

        return contract;
    }

    private static ContractChange Single(IReadOnlyList<ContractChange> changes, string rule)
    {
        var matching = changes.Where(c => c.Rule == rule).ToArray();
        Assert.True(matching.Length == 1,
            $"Expected exactly one '{rule}' change but got: {ContractComparer.Describe(changes)}");
        return matching[0];
    }

    // ------------------------------------------------------------ no change

    [Fact]
    public void An_unchanged_contract_produces_no_changes()
    {
        var contract = ContractWith(("GET /api/projects", Operation()));

        var changes = ContractComparer.Compare(contract, contract);

        Assert.Empty(changes);
    }

    // ----------------------------------------------------------- operations

    [Fact]
    public void Removing_an_operation_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation()));
        var current = new OpenApiContract();

        var change = Single(ContractComparer.Compare(approved, current), "operation-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Adding_an_operation_is_additive()
    {
        var approved = new OpenApiContract();
        var current = ContractWith(("GET /api/projects", Operation()));

        var change = Single(ContractComparer.Compare(approved, current), "operation-added");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    [Fact]
    public void Removing_a_documented_response_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects/{id}", Operation(new[] { "200", "404" })));
        var current = ContractWith(("GET /api/projects/{id}", Operation(new[] { "200" })));

        var change = Single(ContractComparer.Compare(approved, current), "response-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
        Assert.Contains("404", change.Detail);
    }

    [Fact]
    public void Adding_a_documented_response_is_additive()
    {
        var approved = ContractWith(("GET /api/projects/{id}", Operation(new[] { "200" })));
        var current = ContractWith(("GET /api/projects/{id}", Operation(new[] { "200", "404" })));

        var change = Single(ContractComparer.Compare(approved, current), "response-added");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    // ----------------------------------------------------------- parameters

    [Fact]
    public void Removing_a_parameter_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation(parameters: new[] { ("page", false) })));
        var current = ContractWith(("GET /api/projects", Operation()));

        var change = Single(ContractComparer.Compare(approved, current), "parameter-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Adding_an_optional_parameter_is_additive()
    {
        var approved = ContractWith(("GET /api/projects", Operation()));
        var current = ContractWith(("GET /api/projects", Operation(parameters: new[] { ("page", false) })));

        var change = Single(ContractComparer.Compare(approved, current), "parameter-added");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    [Fact]
    public void Adding_a_required_parameter_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation()));
        var current = ContractWith(("GET /api/projects", Operation(parameters: new[] { ("tenant", true) })));

        var change = Single(ContractComparer.Compare(approved, current), "parameter-added");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Making_an_optional_parameter_required_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation(parameters: new[] { ("page", false) })));
        var current = ContractWith(("GET /api/projects", Operation(parameters: new[] { ("page", true) })));

        var change = Single(ContractComparer.Compare(approved, current), "parameter-now-required");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    // --------------------------------------------------------- request body

    [Fact]
    public void Changing_the_request_body_type_is_breaking()
    {
        var approved = ContractWith(("POST /api/projects", Operation(requestBody: "CreateProjectDto")));
        var current = ContractWith(("POST /api/projects", Operation(requestBody: "NewProjectRequest")));

        var change = Single(ContractComparer.Compare(approved, current), "request-body-changed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    // -------------------------------------------------------- authentication

    [Fact]
    public void Newly_requiring_authentication_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation(requiresAuthentication: false)));
        var current = ContractWith(("GET /api/projects", Operation(requiresAuthentication: true)));

        var change = Single(ContractComparer.Compare(approved, current), "authentication-added");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    /// <summary>
    /// Dropping authentication must fail the build.
    /// </summary>
    /// <remarks>
    /// It does not break a client, but it publishes data that was protected. An
    /// endpoint losing its [Authorize] attribute is the most damaging change this
    /// check exists to catch, so it is classified as breaking.
    /// </remarks>
    [Fact]
    public void Dropping_authentication_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects", Operation(requiresAuthentication: true)));
        var current = ContractWith(("GET /api/projects", Operation(requiresAuthentication: false)));

        var change = Single(ContractComparer.Compare(approved, current), "authentication-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Removing_a_required_role_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects/all",
            Operation(requiresAuthentication: true, roles: new[] { "Admin" })));
        var current = ContractWith(("GET /api/projects/all",
            Operation(requiresAuthentication: true)));

        var change = Single(ContractComparer.Compare(approved, current), "role-requirement-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
        Assert.Contains("Admin", change.Detail);
    }

    [Fact]
    public void Adding_a_required_role_is_breaking()
    {
        var approved = ContractWith(("GET /api/projects",
            Operation(requiresAuthentication: true)));
        var current = ContractWith(("GET /api/projects",
            Operation(requiresAuthentication: true, roles: new[] { "Admin" })));

        var change = Single(ContractComparer.Compare(approved, current), "role-requirement-added");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    // -------------------------------------------------------------- schemas

    [Fact]
    public void Removing_a_schema_is_breaking()
    {
        var approved = SchemaContractWith("ProjectDto", new[] { ("id", "integer") });
        var current = new OpenApiContract();

        var change = Single(ContractComparer.Compare(approved, current), "schema-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Removing_a_schema_property_is_breaking()
    {
        var approved = SchemaContractWith("ProjectDto", new[] { ("id", "integer"), ("name", "string") });
        var current = SchemaContractWith("ProjectDto", new[] { ("id", "integer") });

        var change = Single(ContractComparer.Compare(approved, current), "property-removed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
        Assert.Equal("ProjectDto.name", change.Subject);
    }

    [Fact]
    public void Changing_a_property_type_is_breaking()
    {
        var approved = SchemaContractWith("ProjectDto", new[] { ("id", "integer") });
        var current = SchemaContractWith("ProjectDto", new[] { ("id", "string") });

        var change = Single(ContractComparer.Compare(approved, current), "property-type-changed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    /// <summary>
    /// A narrowing of format counts as a type change.
    /// </summary>
    /// <remarks>
    /// <c>string/date-time</c> becoming plain <c>string</c> breaks a generated client
    /// that deserialises the field into a date, even though both are "string".
    /// </remarks>
    [Fact]
    public void Losing_a_property_format_is_breaking()
    {
        var approved = SchemaContractWith("ProjectDto", new[] { ("createdAt", "string/date-time") });
        var current = SchemaContractWith("ProjectDto", new[] { ("createdAt", "string") });

        var change = Single(ContractComparer.Compare(approved, current), "property-type-changed");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Adding_an_optional_property_is_additive()
    {
        var approved = SchemaContractWith("ProjectDto", new[] { ("id", "integer") });
        var current = SchemaContractWith("ProjectDto", new[] { ("id", "integer"), ("archivedAt", "string") });

        var change = Single(ContractComparer.Compare(approved, current), "property-added");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    [Fact]
    public void Adding_a_required_property_is_breaking()
    {
        var approved = SchemaContractWith("CreateProjectDto", new[] { ("name", "string") });
        var current = SchemaContractWith(
            "CreateProjectDto",
            new[] { ("name", "string"), ("tenantId", "string") },
            required: new[] { "tenantId" });

        var change = Single(ContractComparer.Compare(approved, current), "property-added");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Making_an_existing_property_required_is_breaking()
    {
        var approved = SchemaContractWith("CreateProjectDto", new[] { ("status", "string") });
        var current = SchemaContractWith(
            "CreateProjectDto",
            new[] { ("status", "string") },
            required: new[] { "status" });

        var change = Single(ContractComparer.Compare(approved, current), "property-now-required");

        Assert.Equal(ChangeKind.Breaking, change.Kind);
    }

    [Fact]
    public void Relaxing_a_required_property_is_additive()
    {
        var approved = SchemaContractWith(
            "CreateProjectDto", new[] { ("status", "string") }, required: new[] { "status" });
        var current = SchemaContractWith("CreateProjectDto", new[] { ("status", "string") });

        var change = Single(ContractComparer.Compare(approved, current), "property-no-longer-required");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    [Fact]
    public void Adding_a_schema_is_additive()
    {
        var approved = new OpenApiContract();
        var current = SchemaContractWith("ProjectDto", new[] { ("id", "integer") });

        var change = Single(ContractComparer.Compare(approved, current), "schema-added");

        Assert.Equal(ChangeKind.Additive, change.Kind);
    }

    // ------------------------------------------------------------ reporting

    [Fact]
    public void The_report_lists_breaking_changes_before_additive_ones()
    {
        var approved = ContractWith(("GET /api/projects", Operation(new[] { "200", "404" })));
        var current = ContractWith(
            ("GET /api/projects", Operation(new[] { "200", "500" })),
            ("GET /api/projects/all", Operation()));

        var report = ContractComparer.Describe(ContractComparer.Compare(approved, current));

        var breakingIndex = report.IndexOf("BREAKING", StringComparison.Ordinal);
        var additiveIndex = report.IndexOf("additive", StringComparison.Ordinal);

        Assert.True(breakingIndex >= 0 && additiveIndex > breakingIndex,
            $"Expected breaking changes first. Report:{Environment.NewLine}{report}");
    }

    [Fact]
    public void An_empty_comparison_reports_no_changes()
    {
        Assert.Equal("No contract changes.", ContractComparer.Describe(Array.Empty<ContractChange>()));
    }

    [Fact]
    public void A_contract_round_trips_through_json()
    {
        var original = ContractWith(("POST /api/projects",
            Operation(new[] { "201", "400" }, new[] { ("id", true) }, "CreateProjectDto", true)));
        original.Schemas["CreateProjectDto"] = new SchemaContract
        {
            Properties = new SortedDictionary<string, string> { ["name"] = "string" },
            Required = new SortedSet<string> { "name" }
        };

        var restored = OpenApiContract.FromJson(original.ToJson());

        Assert.Empty(ContractComparer.Compare(original, restored));
    }
}
