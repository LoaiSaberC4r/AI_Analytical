using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Middleware;

public sealed class ApiErrors(ILogger<ApiErrors> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception as ApiError;
        if (error is null)
            logger.LogError(exception, "Request failed with trace {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = error?.Status ?? 500;
        var details = new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Detail = error?.Message ?? "An unexpected server error occurred."
        };
        details.Extensions["code"] = error?.Code ?? "server_error";
        return await problems.TryWriteAsync(new() { HttpContext = context, ProblemDetails = details });
    }
}
