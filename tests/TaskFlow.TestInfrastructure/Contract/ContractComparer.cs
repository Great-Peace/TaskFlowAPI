using System.Text;

namespace TaskFlow.TestInfrastructure.Contract;

/// <summary>How a contract change affects existing clients.</summary>
public enum ChangeKind
{
    /// <summary>Existing clients keep working.</summary>
    Additive,

    /// <summary>Existing clients may stop working.</summary>
    Breaking
}

/// <summary>A single difference between the approved contract and the current one.</summary>
/// <param name="Kind">Whether the change breaks existing clients.</param>
/// <param name="Rule">Short identifier of the rule that classified it.</param>
/// <param name="Subject">The operation or schema member affected.</param>
/// <param name="Detail">Human-readable explanation.</param>
public sealed record ContractChange(ChangeKind Kind, string Rule, string Subject, string Detail)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{(Kind == ChangeKind.Breaking ? "BREAKING" : "additive")}  [{Rule}]  {Subject}: {Detail}";
}

/// <summary>
/// Compares an approved API contract with the current one and classifies every
/// difference.
/// </summary>
/// <remarks>
/// <para>
/// The asymmetry is the whole point. Removing something a client may depend on is
/// breaking; adding something a client does not know about is not. That distinction
/// is what keeps the check meaningful: a build only fails when a caller could
/// actually be broken, so a failure is worth reading rather than worth
/// rubber-stamping.
/// </para>
/// <para>
/// <b>Breaking (fails the build):</b> removing an operation, removing a documented
/// response code, removing a schema, removing a schema property, changing a
/// property's type, adding a newly required property or parameter, making an
/// optional parameter required, changing the request body type, or any change to
/// authentication or role requirements in either direction.
/// </para>
/// <para>
/// <b>Additive:</b> adding an operation, response code, schema, optional property or
/// optional parameter; and relaxing a requirement.
/// </para>
/// <para>
/// Authorization is the one category treated as breaking in both directions. Newly
/// requiring authentication or a role breaks existing callers; dropping either does
/// not, but it publishes data that was protected. Both are changes nobody should be
/// able to ship without noticing, so both stop the build.
/// </para>
/// <para>
/// One judgement call is worth naming: adding a required property to a schema is
/// treated as breaking. For a request DTO it plainly is, because existing callers do
/// not send it. For a response DTO it is harmless. The contract does not record which
/// direction a schema travels in, and the two are frequently the same type, so the
/// rule errs toward reporting. A false alarm costs a conversation; a missed break
/// costs a client outage.
/// </para>
/// </remarks>
public static class ContractComparer
{
    /// <summary>Compares <paramref name="current"/> against <paramref name="approved"/>.</summary>
    public static IReadOnlyList<ContractChange> Compare(
        OpenApiContract approved,
        OpenApiContract current)
    {
        var changes = new List<ContractChange>();

        CompareOperations(approved, current, changes);
        CompareSchemas(approved, current, changes);

        return changes;
    }

    private static void CompareOperations(
        OpenApiContract approved,
        OpenApiContract current,
        List<ContractChange> changes)
    {
        foreach (var (key, approvedOperation) in approved.Operations)
        {
            if (!current.Operations.TryGetValue(key, out var currentOperation))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "operation-removed", key,
                    "the operation is no longer exposed"));
                continue;
            }

            foreach (var response in approvedOperation.Responses.Except(currentOperation.Responses))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "response-removed", key,
                    $"response {response} is no longer documented"));
            }

            foreach (var response in currentOperation.Responses.Except(approvedOperation.Responses))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Additive, "response-added", key,
                    $"response {response} was added"));
            }

            CompareParameters(key, approvedOperation, currentOperation, changes);

            if (approvedOperation.RequestBodySchema != currentOperation.RequestBodySchema)
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "request-body-changed", key,
                    $"request body changed from '{approvedOperation.RequestBodySchema ?? "none"}' " +
                    $"to '{currentOperation.RequestBodySchema ?? "none"}'"));
            }

            if (!approvedOperation.RequiresAuthentication && currentOperation.RequiresAuthentication)
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "authentication-added", key,
                    "the operation now requires authentication"));
            }
            else if (approvedOperation.RequiresAuthentication && !currentOperation.RequiresAuthentication)
            {
                // Does not break a client - it can still call the endpoint - but it
                // exposes data that was protected. Silently publishing an endpoint is
                // the worst regression this check can catch, so it fails the build.
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "authentication-removed", key,
                    "the operation NO LONGER requires authentication"));
            }

            foreach (var role in approvedOperation.RequiredRoles.Except(currentOperation.RequiredRoles))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "role-requirement-removed", key,
                    $"the '{role}' role is no longer required"));
            }

            foreach (var role in currentOperation.RequiredRoles.Except(approvedOperation.RequiredRoles))
            {
                // Tightening: existing callers without the role start receiving 403.
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "role-requirement-added", key,
                    $"the '{role}' role is now required"));
            }
        }

        foreach (var key in current.Operations.Keys.Except(approved.Operations.Keys))
        {
            changes.Add(new ContractChange(
                ChangeKind.Additive, "operation-added", key, "a new operation was added"));
        }
    }

    private static void CompareParameters(
        string key,
        OperationContract approved,
        OperationContract current,
        List<ContractChange> changes)
    {
        foreach (var (name, wasRequired) in approved.Parameters)
        {
            if (!current.Parameters.TryGetValue(name, out var isRequired))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "parameter-removed", key,
                    $"parameter '{name}' was removed"));
                continue;
            }

            if (!wasRequired && isRequired)
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "parameter-now-required", key,
                    $"parameter '{name}' became required"));
            }
        }

        foreach (var name in current.Parameters.Keys.Except(approved.Parameters.Keys))
        {
            var kind = current.Parameters[name] ? ChangeKind.Breaking : ChangeKind.Additive;
            var detail = current.Parameters[name]
                ? $"a new required parameter '{name}' was added"
                : $"a new optional parameter '{name}' was added";

            changes.Add(new ContractChange(kind, "parameter-added", key, detail));
        }
    }

    private static void CompareSchemas(
        OpenApiContract approved,
        OpenApiContract current,
        List<ContractChange> changes)
    {
        foreach (var (name, approvedSchema) in approved.Schemas)
        {
            if (!current.Schemas.TryGetValue(name, out var currentSchema))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Breaking, "schema-removed", name, "the schema no longer exists"));
                continue;
            }

            foreach (var (property, approvedType) in approvedSchema.Properties)
            {
                if (!currentSchema.Properties.TryGetValue(property, out var currentType))
                {
                    changes.Add(new ContractChange(
                        ChangeKind.Breaking, "property-removed", $"{name}.{property}",
                        "the property was removed"));
                    continue;
                }

                if (approvedType != currentType)
                {
                    changes.Add(new ContractChange(
                        ChangeKind.Breaking, "property-type-changed", $"{name}.{property}",
                        $"type changed from '{approvedType}' to '{currentType}'"));
                }
            }

            foreach (var property in currentSchema.Properties.Keys.Except(approvedSchema.Properties.Keys))
            {
                var nowRequired = currentSchema.Required.Contains(property);

                changes.Add(new ContractChange(
                    nowRequired ? ChangeKind.Breaking : ChangeKind.Additive,
                    "property-added",
                    $"{name}.{property}",
                    nowRequired
                        ? "a new required property was added"
                        : "a new optional property was added"));
            }

            foreach (var property in currentSchema.Required.Except(approvedSchema.Required))
            {
                // Only report properties that already existed; newly added required
                // properties are reported once, above.
                if (approvedSchema.Properties.ContainsKey(property))
                {
                    changes.Add(new ContractChange(
                        ChangeKind.Breaking, "property-now-required", $"{name}.{property}",
                        "an optional property became required"));
                }
            }

            foreach (var property in approvedSchema.Required.Except(currentSchema.Required))
            {
                changes.Add(new ContractChange(
                    ChangeKind.Additive, "property-no-longer-required", $"{name}.{property}",
                    "a required property became optional"));
            }
        }

        foreach (var name in current.Schemas.Keys.Except(approved.Schemas.Keys))
        {
            changes.Add(new ContractChange(
                ChangeKind.Additive, "schema-added", name, "a new schema was added"));
        }
    }

    /// <summary>Renders <paramref name="changes"/> as a report, breaking changes first.</summary>
    public static string Describe(IReadOnlyList<ContractChange> changes)
    {
        if (changes.Count == 0)
        {
            return "No contract changes.";
        }

        var builder = new StringBuilder();
        var breaking = changes.Where(c => c.Kind == ChangeKind.Breaking).ToArray();
        var additive = changes.Where(c => c.Kind == ChangeKind.Additive).ToArray();

        if (breaking.Length > 0)
        {
            builder.AppendLine($"{breaking.Length} breaking change(s):");
            foreach (var change in breaking)
            {
                builder.Append("  ").AppendLine(change.ToString());
            }
        }

        if (additive.Length > 0)
        {
            builder.AppendLine($"{additive.Length} additive change(s):");
            foreach (var change in additive)
            {
                builder.Append("  ").AppendLine(change.ToString());
            }
        }

        return builder.ToString();
    }
}
