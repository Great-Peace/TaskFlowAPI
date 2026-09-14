using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskFlow.TestInfrastructure.Contract;

/// <summary>
/// A schema's shape, reduced to the parts a client can break against.
/// </summary>
public sealed class SchemaContract
{
    /// <summary>Property name to its OpenAPI type, for example <c>string</c> or <c>integer</c>.</summary>
    public SortedDictionary<string, string> Properties { get; init; } = new();

    /// <summary>Properties the schema marks as required.</summary>
    public SortedSet<string> Required { get; init; } = new();
}

/// <summary>
/// An operation's shape, reduced to the parts a client can break against.
/// </summary>
public sealed class OperationContract
{
    /// <summary>Status codes the operation documents.</summary>
    public SortedSet<string> Responses { get; init; } = new();

    /// <summary>Parameter name to whether it is required.</summary>
    public SortedDictionary<string, bool> Parameters { get; init; } = new();

    /// <summary>Schema name of the request body, if any.</summary>
    public string? RequestBodySchema { get; init; }

    /// <summary>
    /// Whether the operation requires an authenticated caller.
    /// </summary>
    /// <remarks>
    /// Taken from the endpoint's authorization metadata, not from the OpenAPI
    /// document. The document declares a single document-wide security scheme, so
    /// every operation appears to require authentication there, including the
    /// anonymous login and register routes.
    /// </remarks>
    public bool RequiresAuthentication { get; init; }

    /// <summary>Roles the caller must hold, if the operation is role-restricted.</summary>
    public SortedSet<string> RequiredRoles { get; init; } = new();
}

/// <summary>
/// A comparable projection of the API's OpenAPI document.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not the whole document. A full-file snapshot treats every addition,
/// description edit and property reordering as a difference, so the baseline needs
/// updating constantly and reviewers learn to approve the diff without reading it.
/// A check that is always approved protects nothing.
/// </para>
/// <para>
/// What is kept is what a client can actually break against: which operations exist,
/// which status codes they document, which parameters they take, the shape of each
/// schema, and whether authentication is required. Summaries, descriptions, examples
/// and tags are excluded, because changing them cannot break a caller.
/// </para>
/// </remarks>
public sealed class OpenApiContract
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Operations, keyed as <c>METHOD /path</c>.</summary>
    public SortedDictionary<string, OperationContract> Operations { get; init; } = new();

    /// <summary>Schemas, keyed by name.</summary>
    public SortedDictionary<string, SchemaContract> Schemas { get; init; } = new();

    /// <summary>Serialises the contract for storage as a baseline.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Reads a stored baseline.</summary>
    public static OpenApiContract FromJson(string json) =>
        JsonSerializer.Deserialize<OpenApiContract>(json, SerializerOptions)
        ?? throw new InvalidOperationException("The stored contract baseline could not be parsed.");
}
