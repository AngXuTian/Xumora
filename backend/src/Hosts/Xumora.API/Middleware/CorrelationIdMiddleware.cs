using System.Diagnostics;
using Serilog.Context;

namespace Xumora.API.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetCorrelationId(context);
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values)
            && values.Count == 1
            && IsValid(values[0]))
        {
            return values[0]!;
        }

        return Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier;
    }

    private static bool IsValid(string? value)
    {
        return value is { Length: > 0 and <= 64 }
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.');
    }
}
