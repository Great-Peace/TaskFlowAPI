using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TaskFlow.TestInfrastructure.Contract;

/// <summary>Authorization requirements of a single endpoint.</summary>
/// <param name="RequiresAuthentication">Whether an authenticated caller is required.</param>
/// <param name="RequiredRoles">Roles the caller must hold, if any.</param>
public sealed record EndpointAuthorization(bool RequiresAuthentication, SortedSet<string> RequiredRoles);

/// <summary>
/// Reads each endpoint's real authorization requirements from routing metadata.
/// </summary>
/// <remarks>
/// The OpenAPI document cannot answer this. The application declares one
/// document-wide security requirement, so every operation in the document looks
/// authenticated - including <c>POST /api/Auth/login</c>, which is deliberately
/// anonymous. Reading <see cref="IAuthorizeData"/> and <see cref="IAllowAnonymous"/>
/// from the endpoint metadata reports what the framework will actually enforce,
/// which is what a regression would change.
/// </remarks>
public static class EndpointAuthorizationReader
{
    /// <summary>Strips inline route constraints, turning <c>{id:int}</c> into <c>{id}</c>.</summary>
    private static readonly Regex RouteConstraint = new(@"\{(\w+)(?::[^}]+)?\}", RegexOptions.Compiled);

    /// <summary>
    /// Maps <c>METHOD /path</c> to the authorization the framework enforces for it.
    /// </summary>
    public static IReadOnlyDictionary<string, EndpointAuthorization> Read(IServiceProvider services)
    {
        var result = new Dictionary<string, EndpointAuthorization>(StringComparer.OrdinalIgnoreCase);

        var endpointSources = services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        foreach (var endpoint in endpointSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is null || methods.Count == 0)
            {
                continue;
            }

            // An [AllowAnonymous] anywhere in the metadata chain wins over [Authorize],
            // which is exactly how the authorization middleware resolves it.
            var allowsAnonymous = endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0;
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

            var roles = authorizeData
                .Select(data => data.Roles)
                .Where(roles => !string.IsNullOrWhiteSpace(roles))
                .SelectMany(roles => roles!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var authorization = new EndpointAuthorization(
                RequiresAuthentication: authorizeData.Count > 0 && !allowsAnonymous,
                RequiredRoles: new SortedSet<string>(allowsAnonymous ? Array.Empty<string>() : roles));

            var path = NormalisePath(endpoint.RoutePattern.RawText);

            foreach (var method in methods)
            {
                result[$"{method.ToUpperInvariant()} {path}"] = authorization;
            }
        }

        return result;
    }

    /// <summary>
    /// Renders a route pattern the way the OpenAPI document spells it, so the two can
    /// be matched: leading slash, and route constraints removed.
    /// </summary>
    private static string NormalisePath(string? rawText)
    {
        var path = rawText ?? string.Empty;

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return RouteConstraint.Replace(path, "{$1}");
    }
}
