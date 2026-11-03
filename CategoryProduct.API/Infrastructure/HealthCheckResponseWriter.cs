using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CategoryProduct.API.Infrastructure
{
    public static class HealthCheckResponseWriter
    {
        public static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            var body = new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1)
                }),
                totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1)
            };
            return context.Response.WriteAsync(
                JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }
}
