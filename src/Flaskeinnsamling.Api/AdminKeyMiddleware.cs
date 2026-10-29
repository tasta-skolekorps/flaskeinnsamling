using System.Security.Cryptography;
using System.Text;

namespace Flaskeinnsamling.Api;

/// <summary>
/// Requires a valid X-Admin-Key header on every non-GET /api request. If Admin:ApiKey is not
/// configured, all writes are rejected. The key is never logged.
/// </summary>
public sealed class AdminKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<AdminKeyMiddleware> logger)
{
    public const string HeaderName = "X-Admin-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresAdmin(context.Request) && !IsAuthorized(context.Request, configuration))
        {
            logger.LogWarning("Rejected unauthorized {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(context);
    }

    private static bool RequiresAdmin(HttpRequest request) =>
        request.Path.StartsWithSegments("/api") &&
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));

    public static bool HasKey(HttpRequest request) => !string.IsNullOrEmpty(request.Headers[HeaderName].ToString());

    public static bool IsAuthorized(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration["Admin:ApiKey"];
        if (string.IsNullOrEmpty(expected)) return false;

        var provided = request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(provided)) return false;

        // Hash both sides so the comparison is constant-time regardless of length.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
