using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CategoryProduct.MVC.Services
{
    public class ApiUnavailableException(string message, Exception? inner = null) : Exception(message, inner)
    {
        public const string FriendlyMessage = "The service is unavailable. Please try again later.";
    }

    // Logs and converts API connection failures and 5xx responses into ApiUnavailableException.
    public class ApiErrorHandler(ILogger<ApiErrorHandler> logger) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogError(ex, "API unreachable: {Method} {Uri}", request.Method, request.RequestUri);
                throw new ApiUnavailableException(ApiUnavailableException.FriendlyMessage, ex);
            }

            if ((int)response.StatusCode >= 500)
            {
                logger.LogError("API returned {StatusCode} for {Method} {Uri}", (int)response.StatusCode, request.Method, request.RequestUri);
                response.Dispose();
                throw new ApiUnavailableException(ApiUnavailableException.FriendlyMessage);
            }
            return response;
        }
    }

    // Shows a friendly page instead of a raw exception when the API is down.
    public class ApiUnavailableFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is not ApiUnavailableException ex) return;
            context.Result = new ViewResult
            {
                ViewName = "ServiceUnavailable",
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                    context.ModelState) { ["Message"] = ex.Message }
            };
            context.ExceptionHandled = true;
        }
    }
}
