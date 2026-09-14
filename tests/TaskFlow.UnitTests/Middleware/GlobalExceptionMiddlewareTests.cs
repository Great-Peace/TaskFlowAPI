using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFlow.API.Middleware;
using TaskFlow.Core.DTOs;

namespace TaskFlow.UnitTests.Middleware;

/// <summary>
/// How unhandled exceptions are translated into HTTP responses.
/// </summary>
/// <remarks>
/// This is the application's last line of defence: whatever escapes a controller
/// becomes a response shaped here. The mapping of exception type to status code is
/// behaviour clients depend on, and the wording of the fallback response is a
/// security property.
/// </remarks>
public class GlobalExceptionMiddlewareTests
{
    private static async Task<(HttpStatusCode Status, string Body)> InvokeWith(Exception? thrown)
    {
        var context = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        RequestDelegate next = _ => thrown is null ? Task.CompletedTask : throw thrown;

        var middleware = new GlobalExceptionMiddleware(
            next,
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        responseBody.Seek(0, SeekOrigin.Begin);
        var body = Encoding.UTF8.GetString(responseBody.ToArray());

        return ((HttpStatusCode)context.Response.StatusCode, body);
    }

    [Fact]
    public async Task A_successful_request_passes_through_untouched()
    {
        var (status, body) = await InvokeWith(null);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Unauthorized_access_becomes_401()
    {
        var (status, body) = await InvokeWith(new UnauthorizedAccessException("no access for you"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Unauthorized access", Deserialize(body).Message);
    }

    [Fact]
    public async Task Argument_exceptions_become_400_and_keep_their_message()
    {
        // ArgumentException is used for caller mistakes, so its message is safe to
        // return and is useful to the client.
        var (status, body) = await InvokeWith(new ArgumentException("startDate must precede endDate"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("startDate must precede endDate", Deserialize(body).Message);
    }

    [Fact]
    public async Task Missing_resources_become_404()
    {
        var (status, body) = await InvokeWith(new KeyNotFoundException("project 7 is gone"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Resource not found", Deserialize(body).Message);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(TimeoutException))]
    public async Task Unexpected_exceptions_become_500(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        var (status, _) = await InvokeWith(exception);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
    }

    /// <summary>
    /// The fallback response must not disclose internal detail.
    /// </summary>
    /// <remarks>
    /// Exception messages routinely contain connection strings, file paths, SQL and
    /// type names. Returning them to an unauthenticated caller is an information
    /// disclosure, so the 500 response is deliberately uninformative.
    /// </remarks>
    [Fact]
    public async Task An_unexpected_exception_does_not_leak_its_details()
    {
        var revealing = new InvalidOperationException(
            "Login failed for user 'sa'. Server=prod-sql-01;Password=hunter2");

        var (status, body) = await InvokeWith(revealing);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("An internal server error occurred", Deserialize(body).Message);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("prod-sql-01", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task Error_responses_are_camel_cased_json()
    {
        var (_, body) = await InvokeWith(new KeyNotFoundException());

        Assert.Contains("\"message\"", body);
        Assert.DoesNotContain("\"Message\"", body);
    }

    private static ErrorResponseDto Deserialize(string body) =>
        JsonSerializer.Deserialize<ErrorResponseDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException($"Could not deserialise the error response: {body}");
}
