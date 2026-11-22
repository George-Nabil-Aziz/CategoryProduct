using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CategoryProduct.API.Infrastructure
{
    public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ProblemDetails problem;
            int status;
            if (exception is BadHttpRequestException bad)
            {
                status = bad.StatusCode;
                logger.LogWarning(bad, "Bad request for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Title = status == StatusCodes.Status413PayloadTooLarge ? "Request body too large." : "Bad request.",
                    Status = status
                };
            }
            else
            {
                status = StatusCodes.Status500InternalServerError;
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Title = "An unexpected error occurred.",
                    Status = status
                };
            }
            problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

            httpContext.Response.StatusCode = status;
            await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
            return true;
        }
    }
}
