using Serilog;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text;
using CategoryProduct.API.Data;
using CategoryProduct.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");
builder.Services.AddExceptionHandler<CategoryProduct.API.Infrastructure.GlobalExceptionHandler>();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 characters. Set it with " +
        "'dotnet user-secrets set \"Jwt:Key\" \"<key>\"' (development) or the 'Jwt__Key' environment variable.");
}
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRouting(o => o.LowercaseUrls = true);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the JWT returned by the login endpoint."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? string.Empty))
        };
    });
builder.Services.AddAuthorization();

var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 5);
var registerPermitLimit = builder.Configuration.GetValue("RateLimiting:RegisterPermitLimit", 5);
var logoutPermitLimit = builder.Configuration.GetValue("RateLimiting:LogoutPermitLimit", 30);
var refreshPermitLimit = builder.Configuration.GetValue("RateLimiting:RefreshPermitLimit", 30);
var profilePermitLimit = builder.Configuration.GetValue("RateLimiting:ProfilePermitLimit", 30);
var forgotPasswordPermitLimit = builder.Configuration.GetValue("RateLimiting:ForgotPasswordPermitLimit", 5);
var passwordPermitLimit = builder.Configuration.GetValue("RateLimiting:PasswordPermitLimit", 5);
var paymentsPermitLimit = builder.Configuration.GetValue("RateLimiting:PaymentsPermitLimit", 10);
builder.Services.Configure<CategoryProduct.API.Services.ProductImageOptions>(
    builder.Configuration.GetSection(CategoryProduct.API.Services.ProductImageOptions.SectionName));
builder.Services.AddSingleton<CategoryProduct.API.Services.IProductImageStorage, CategoryProduct.API.Services.ProductImageStorage>();
builder.Services.Configure<CategoryProduct.API.Services.InventoryOptions>(
    builder.Configuration.GetSection(CategoryProduct.API.Services.InventoryOptions.SectionName));
builder.Services.AddScoped<CategoryProduct.API.Services.InventoryService>();
builder.Services.AddScoped<CategoryProduct.API.Services.TokenService>();
builder.Services.AddScoped<CategoryProduct.API.Services.RefreshTokenCleanupService>();
builder.Services.AddHostedService<CategoryProduct.API.Services.RefreshTokenCleanupWorker>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddEmailNotifications(builder.Configuration);
builder.Services.AddScoped<CategoryProduct.API.Services.NotificationService>();
var paymentOptions = CategoryProduct.API.Services.PaymentOptions.Create(
    builder.Configuration.GetSection(CategoryProduct.API.Services.PaymentOptions.SectionName));
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(paymentOptions));
switch (paymentOptions.Provider)
{
    case CategoryProduct.API.Services.PaymentOptions.FakeProvider:
        builder.Services.AddSingleton<CategoryProduct.API.Services.IPaymentGateway, CategoryProduct.API.Services.FakePaymentGateway>();
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Payments:Provider '{paymentOptions.Provider}'. Supported: {CategoryProduct.API.Services.PaymentOptions.FakeProvider}.");
}
builder.Services.AddScoped<CategoryProduct.API.Services.PaymentService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = registerPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = refreshPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("profile", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = profilePermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("logout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = logoutPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("forgot-password", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = forgotPasswordPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("payments", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = paymentsPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("password", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = passwordPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

if (paymentOptions.Provider == CategoryProduct.API.Services.PaymentOptions.FakeProvider)
    app.Logger.LogWarning("Fake payment gateway active: no real money moves");

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex != null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Configuration.GetValue("Hosting:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = CategoryProduct.API.Infrastructure.HealthCheckResponseWriter.WriteAsync
}).AllowAnonymous();

await DatabaseMigrator.MigrateIfEnabledAsync(app.Services, app.Configuration, app.Logger);
await BootstrapAdmin.EnsureIfConfiguredAsync(app.Services, app.Configuration, app.Logger);

app.Run();

public partial class Program { }
