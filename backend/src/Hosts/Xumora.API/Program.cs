using Xumora.API.Handlers;
using Microsoft.EntityFrameworkCore;
using Xumora.Infrastructure.Shared.Persistence;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Xumora.Photography.Application.DependencyInjection;
using Xumora.Photography.Infrastructure.DependencyInjection;
using Asp.Versioning;
using Xumora.API.Configurations;
using Xumora.API.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Microsoft.Extensions.Options;
using Xumora.Identity.Infrastructure;
using Xumora.Identity.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

var openTelemetry = builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService("Xumora.API"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Xumora.API"));

if (!string.IsNullOrWhiteSpace(otelEndpoint))
{
    if (!Uri.TryCreate(otelEndpoint, UriKind.Absolute, out var endpoint))
    {
        throw new InvalidOperationException(
            "OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute URI.");
    }

    openTelemetry.WithTracing(tracing =>
        tracing.AddOtlpExporter(options => options.Endpoint = endpoint));
}

// Add services to the container.
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressModelStateInvalidFilter = false;
    });

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);

        // Require clients to specify the version in the URL.
        options.AssumeDefaultVersionWhenUnspecified = false;

        // Include supported and deprecated versions in response headers.
        options.ReportApiVersions = true;

        // Read the API version from the URL segment.
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi(options =>
    {
        options.Document.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Xumora API";
            document.Info.Description = """
                Xumora - Your Personal AI Photographer.

                API for photography analysis, composition recommendations,
                camera guidance and AI-powered photography assistance.
                """;

            return Task.CompletedTask;
        });
    });

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddIdentityInfrastructure();

builder.Services.AddDbContext<XumoraDbContext>((serviceProvider, options) =>
{
    var connectionString = serviceProvider
        .GetRequiredService<IOptions<DatabaseOptions>>()
        .Value
        .DefaultConnection;

    options.UseNpgsql(connectionString);
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter =
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var ipAddress = httpContext.Connection.RemoteIpAddress?
                .ToString() ?? "unknown";

            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: ipAddress,
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });

    options.OnRejected = async (context, _) =>
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "You have sent too many requests. Please try again later.",
            Instance = context.HttpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] =
            context.HttpContext.TraceIdentifier;
        problemDetails.Extensions["correlationId"] =
            context.HttpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();

        await context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = problemDetails
            });
    };
});

// Photography Module
builder.Services.AddPhotographyApplication();
builder.Services.AddPhotographyInfrastructure();

builder.Services
    .AddOptions<AIOptions>()
    .BindConfiguration(AIOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
    
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var identityDb = scope.ServiceProvider
        .GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set(
            "CorrelationId",
            httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    };
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Xumora API v1");

        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
