using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SphereBackend.Infrastructure.Http
{
    internal sealed class ApiProblemDetailsResultFilter : IAlwaysRunResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not ObjectResult objectResult)
            {
                return;
            }

            var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
            if (statusCode < StatusCodes.Status400BadRequest)
            {
                return;
            }

            if (objectResult.Value is ProblemDetails problem)
            {
                problem.Status ??= statusCode;
                problem.Title ??= GetTitle(statusCode);
                problem.Instance ??= context.HttpContext.Request.Path;

                if (!problem.Extensions.ContainsKey("traceId"))
                {
                    problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                }

                return;
            }

            var message = TryExtractMessage(objectResult.Value);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            context.Result = new ObjectResult(ApiProblemDetailsFactory.Create(statusCode, message, context.HttpContext))
            {
                StatusCode = statusCode
            };
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }

        private static string? TryExtractMessage(object? value)
        {
            if (value is string message)
            {
                return message;
            }

            if (value is null)
            {
                return null;
            }

            var messageProperty = value.GetType().GetProperty("message")
                ?? value.GetType().GetProperty("Message");

            if (messageProperty?.PropertyType != typeof(string))
            {
                return null;
            }

            return messageProperty.GetValue(value) as string;
        }

        private static string GetTitle(int statusCode) => statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            StatusCodes.Status500InternalServerError => "Internal Server Error",
            _ => "Request Failed"
        };
    }
}
