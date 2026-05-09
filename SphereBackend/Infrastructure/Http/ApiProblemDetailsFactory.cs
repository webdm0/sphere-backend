using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Net.Http.Headers;
using System.Globalization;

namespace SphereBackend.Infrastructure.Http
{
    internal static class ApiProblemDetailsFactory
    {
        public static ProblemDetails Create(int statusCode, string detail, HttpContext httpContext)
        {
            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),
                Detail = detail,
                Instance = httpContext.Request.Path
            };

            problem.Extensions["traceId"] = httpContext.TraceIdentifier;
            return problem;
        }

        public static ProblemDetails CreateTooManyRequests(
            string detail,
            DateTime retryAfterUtc,
            HttpContext httpContext)
        {
            var problem = Create(StatusCodes.Status429TooManyRequests, detail, httpContext);
            var retryAfterSeconds = GetRetryAfterSeconds(retryAfterUtc);

            problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;
            problem.Extensions["retryAfterUtc"] = retryAfterUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            httpContext.Response.Headers[HeaderNames.RetryAfter] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            return problem;
        }

        public static ValidationProblemDetails CreateValidation(
            ModelStateDictionary modelState,
            HttpContext httpContext)
        {
            var problem = new ValidationProblemDetails(modelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Detail = "See the errors field for details.",
                Instance = httpContext.Request.Path
            };

            problem.Extensions["traceId"] = httpContext.TraceIdentifier;
            return problem;
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

        private static int GetRetryAfterSeconds(DateTime retryAfterUtc)
        {
            var remaining = retryAfterUtc.ToUniversalTime() - DateTime.UtcNow;
            return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        }
    }
}
