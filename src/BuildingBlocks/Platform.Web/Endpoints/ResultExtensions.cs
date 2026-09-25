using Microsoft.AspNetCore.Http;
using Platform.SharedKernel.Results;

namespace Platform.Web.Endpoints;

/// <summary>Maps application <see cref="Result"/>s to HTTP responses (RFC 9457 problem details).</summary>
public static class ResultExtensions
{
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem();

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();

    public static IResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Details is not null)
        {
            extensions["errors"] = error.Details;
        }

        return TypedResults.Problem(
            title: error.Type == ErrorType.Validation ? "One or more validation errors occurred." : error.Description,
            detail: error.Type == ErrorType.Validation ? error.Description : null,
            statusCode: status,
            extensions: extensions);
    }
}
