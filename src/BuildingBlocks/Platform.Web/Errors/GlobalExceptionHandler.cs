using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Domain;

namespace Platform.Web.Errors;

/// <summary>Last-resort handler: converts unhandled exceptions to problem details without leaking internals.</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified by someone else. Reload and try again.", "concurrency_conflict"),
            DomainException domain => (StatusCodes.Status422UnprocessableEntity, domain.Message, "domain_rule_violated"),
            BadHttpRequestException bad => (bad.StatusCode, "The request could not be read.", "bad_request"),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Client closed request.", "cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "internal_error"),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Extensions = { ["code"] = code } },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
