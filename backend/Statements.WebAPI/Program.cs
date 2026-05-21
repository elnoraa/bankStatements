using System.Text;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Statements.WebAPI.Auth;
using Statements.WebAPI.Data;
using Statements.WebAPI.Hubs;
using Statements.WebAPI.Services.Analysis;
using Statements.WebAPI.Services.Auth;
using Statements.WebAPI.Services.BankAccounts;
using Statements.WebAPI.Services.Messaging;
using Statements.WebAPI.Services.Export;
using Statements.WebAPI.Services.Statements;

// Register Dapper type handlers for DateOnly (required for Npgsql compatibility)

SqlMapper.AddTypeHandler(new Statements.WebAPI.Infrastructure.DateOnlyHandler());
SqlMapper.AddTypeHandler(new Statements.WebAPI.Infrastructure.NullableDateOnlyHandler());

var builder = WebApplication.CreateBuilder(args);

// Clean up old log files so each container restart starts fresh
var logsDir = Path.Combine("Logs");
if (Directory.Exists(logsDir))
{
    foreach (var oldLog in Directory.GetFiles(logsDir, "*.log"))
    {
        try { File.Delete(oldLog); } catch { /* best-effort */ }
    }
}

// Configure Serilog (sinks defined in appsettings.json Serilog section)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Statements.WebAPI")
    .CreateLogger();

builder.Host.UseSerilog();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .WithHeaders("Authorization", "Content-Type", "X-Requested-With")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowCredentials();
    });
});

// Add rate limiting for auth endpoints (IP-partitioned)
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict: login/register — 2 req / 15 min per IP
    rateLimiterOptions.AddPolicy("AuthStrict", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Moderate: external/code (OAuth callback) — 10 req / 15 min per IP
    rateLimiterOptions.AddPolicy("AuthModerate", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Default: refresh/external — 5 req / 15 min per IP
    rateLimiterOptions.AddPolicy("AuthDefault", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // General API rate limit (global, not IP-partitioned)
    rateLimiterOptions.AddFixedWindowLimiter("Api", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });
});
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddScoped<IDbExecutor, DapperDbExecutor>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IExternalAuthValidator, ExternalAuthValidator>();
builder.Services.AddHttpClient("external-auth");
builder.Services.AddScoped<IBankAccountService, BankAccountService>();
builder.Services.AddScoped<IStatementService, StatementService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ICsvExportService, CsvExportService>();
builder.Services.AddTransient<IStatementParser, PdfStatementParser>();
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.AddSingleton<IVirusScanService, ClamAvVirusScanService>();

// RabbitMQ for background statement processing
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
builder.Services.AddScoped<ProcessStatementConsumer>();
builder.Services.AddHostedService<StatementProcessingBackgroundService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret))
        };

        // Allow SignalR to receive the access token via query string (WebSocket doesn't support headers)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Startup validation: fail fast if required secrets are missing or placeholder
ValidateConfiguration(builder.Configuration, app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (!builder.Configuration.GetValue<bool>("DOTNET_RUNNING_IN_CONTAINER"))
{
    app.UseHttpsRedirection();
}

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "0"); // Deprecated but still scanned by some tools
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");

    // Content-Security-Policy: restrict script/style sources
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'");

    // HSTS (only when HTTPS is used)
    if (!string.IsNullOrEmpty(context.Request.Scheme) &&
        context.Request.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.Append("Strict-Transport-Security",
            "max-age=31536000; includeSubDomains");
    }

    await next();
});

// Global exception handler: never leak exception details to clients
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            """{"error":"An unexpected error occurred. Please try again later."}""");
    });
});

app.UseCookiePolicy();
app.UseRateLimiter();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<StatementProcessingHub>("/hubs/statement-processing");

app.Run();

/// <summary>
/// Validates that required configuration values (JWT secret, connection string) are present and meet minimum requirements.
/// Throws at startup if critical configuration is missing.
/// </summary>
/// <param name="configuration">The application configuration.</param>
/// <param name="logger">Logger instance.</param>
/// <exception cref="InvalidOperationException">Thrown when required configuration is missing.</exception>
static void ValidateConfiguration(IConfiguration configuration, Microsoft.Extensions.Logging.ILogger logger)
{
    var requiredVars = new (string Key, string Name)[]
    {
        ("Jwt:Secret", "Jwt__Secret"),
        ("ConnectionStrings:DefaultConnection", "ConnectionStrings__DefaultConnection")
    };

    var placeholderVars = new (string Key, string Placeholder)[]
    {
        ("Jwt:Secret", ""),
    };

    bool hasErrors = false;

    foreach (var (key, name) in requiredVars)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            logger.LogError("Missing required configuration: {Name}. Set it via appsettings, environment variable, or user secrets.", name);
            hasErrors = true;
        }
    }

    // Check for placeholder/empty JWT secret
    var jwtSecret = configuration["Jwt:Secret"];
    if (jwtSecret == null || jwtSecret.Length < 32)
    {
        logger.LogWarning("JWT secret is too short (minimum 32 characters). Generate a strong secret with: dotnet user-secrets set \"Jwt:Secret\" \"<64-char-random-string>\"");
    }

    if (hasErrors)
    {
        // In production, this prevents the app from starting with missing configuration
        throw new InvalidOperationException("Required configuration is missing. The application cannot start.");
    }

    logger.LogInformation("Configuration validated successfully.");
}
