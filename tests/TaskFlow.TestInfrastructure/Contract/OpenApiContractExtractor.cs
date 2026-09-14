using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace TaskFlow.TestInfrastructure.Contract;

/// <summary>
/// Produces an <see cref="OpenApiContract"/> from the application under test.
/// </summary>
/// <remarks>
/// The document is generated in-process through Swashbuckle's
/// <see cref="ISwaggerProvider"/>, resolved from the application's own service
/// provider. This matters for two reasons. First, no production code changes: the
/// application only serves the Swagger endpoint in Development, but
/// <c>AddSwaggerGen</c> is registered unconditionally, so generation is always
/// available even where the endpoint is not exposed. Second, no HTTP call and no
/// listening server are required, so the contract check runs as a fast unit-style
/// test rather than needing the full integration harness.
/// </remarks>
public static class OpenApiContractExtractor
{
    /// <summary>The document name the application registers.</summary>
    public const string DocumentName = "v1";

    /// <summary>Generates the current contract from <paramref name="services"/>.</summary>
    public static OpenApiContract Extract(IServiceProvider services)
    {
        var provider = services.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger(DocumentName);
        var authorization = EndpointAuthorizationReader.Read(services);

        return Extract(document, authorization);
    }

    /// <summary>Reduces an OpenAPI document to its comparable contract.</summary>
    /// <param name="document">The generated OpenAPI document.</param>
    /// <param name="authorization">
    /// Real authorization requirements per operation, keyed as <c>METHOD /path</c>.
    /// Supplied separately because the document cannot express them: it declares one
    /// document-wide security scheme that applies to anonymous operations too.
    /// </param>
    public static OpenApiContract Extract(
        OpenApiDocument document,
        IReadOnlyDictionary<string, EndpointAuthorization>? authorization = null)
    {
        var contract = new OpenApiContract();

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var key = $"{operationType.ToString().ToUpperInvariant()} {path}";

                var endpointAuth = authorization is not null && authorization.TryGetValue(key, out var found)
                    ? found
                    : null;

                contract.Operations[key] = new OperationContract
                {
                    Responses = new SortedSet<string>(operation.Responses.Keys),
                    Parameters = new SortedDictionary<string, bool>(
                        operation.Parameters.ToDictionary(p => p.Name, p => p.Required)),
                    RequestBodySchema = ResolveRequestBodySchema(operation),
                    RequiresAuthentication = endpointAuth?.RequiresAuthentication ?? false,
                    RequiredRoles = endpointAuth?.RequiredRoles ?? new SortedSet<string>()
                };
            }
        }

        foreach (var (name, schema) in document.Components.Schemas)
        {
            contract.Schemas[name] = new SchemaContract
            {
                Properties = new SortedDictionary<string, string>(
                    schema.Properties.ToDictionary(p => p.Key, p => DescribeType(p.Value))),
                Required = new SortedSet<string>(schema.Required)
            };
        }

        return contract;
    }

    private static string? ResolveRequestBodySchema(OpenApiOperation operation)
    {
        var content = operation.RequestBody?.Content;
        if (content is null || content.Count == 0)
        {
            return null;
        }

        var schema = content.TryGetValue("application/json", out var json)
            ? json.Schema
            : content.Values.First().Schema;

        return schema is null ? null : DescribeType(schema);
    }

    /// <summary>
    /// Describes a schema by the identity a client depends on.
    /// </summary>
    /// <remarks>
    /// A reference is described by the name it points at, an array by its item type,
    /// and everything else by type and format. Format is included because
    /// <c>string/date-time</c> changing to plain <c>string</c> is a real break for a
    /// typed client even though both are "string".
    /// </remarks>
    private static string DescribeType(OpenApiSchema schema)
    {
        if (schema.Reference is not null)
        {
            return schema.Reference.Id;
        }

        if (schema.Type == "array" && schema.Items is not null)
        {
            return $"array<{DescribeType(schema.Items)}>";
        }

        return string.IsNullOrEmpty(schema.Format)
            ? schema.Type ?? "object"
            : $"{schema.Type}/{schema.Format}";
    }
}
