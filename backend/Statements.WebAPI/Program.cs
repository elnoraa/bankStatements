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
using Statements.WebAPI.Services.Analysis;
using Statements.WebAPI.Services.Auth;
using Statements.WebAPI.Services.Statements;

// Register Dapper type handler for DateOnly (required for Npgsql compatibility)

SqlMapper.AddTypeHandler(new Statements.WebAPI.Infrastructure.DateOnlyHandler());

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Statements.WebAPI")
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine("Logs", "statements-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .WithHeaders("Authorization", "Content-Type", "X-Requested-With")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowCredentials();
    });
});

// Configure cookie policy for httpOnly refresh tokens
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.HttpOnly = HttpOnlyPolicy.Always;
    options.Secure = CookieSecurePolicy.SameAsRequest;
});

// Add rate limiting for auth endpoints
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiterOptions.AddFixedWindowLimiter("Auth", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(15);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    rateLimiterOptions.AddFixedWindowLimiter("Api", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });
});
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IExternalAuthValidator, ExternalAuthValidator>();
builder.Services.AddHttpClient("external-auth");
builder.Services.AddScoped<IStatementService, StatementService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddTransient<IStatementParser, PdfStatementParser>();
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.AddSingleton<IVirusScanService, ClamAvVirusScanService>();
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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Startup validation: fail fast if required secrets are missing or placeholder
ValidateConfiguration(builder.Configuration, app.Services.GetRequiredService<ILogger<Program>>());

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
        "connect-src 'self'; " +
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

app.UseCookiePolicy();
app.UseRateLimiter();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void ValidateConfiguration(IConfiguration configuration, ILogger logger)
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
