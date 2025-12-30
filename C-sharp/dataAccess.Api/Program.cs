using dataAccess.Api;
using dataAccess.Api.Middleware;
using dataAccess.Api.Services;
using dataAccess.Services;
using dataAccess.Planning;
using dataAccess.Planning.Nlq;
using dataAccess.Reports;
using dataAccess.Forecasts;
using dataAccess.LLM;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Npgsql;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var builder = WebApplication.CreateBuilder(args);

// -------------------------------
// Config & Services
builder.Services.AddSingleton<Shared.Allowlists.ISqlAllowlist, Shared.Allowlists.SqlAllowlistV2>();
// -------------------------------
var candidates = new[] {
    Path.Combine(AppContext.BaseDirectory, ".env"),                 // bin/Debug/netX/ with .env copied (optional)
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),          // working dir (VS launches here by default)
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env")// when running from bin to project root
};
foreach (var p in candidates) { if (File.Exists(p)) { Env.Load(p); break; } }

builder.Configuration.AddEnvironmentVariables();

// Normalize legacy connection string keys into the new ConnectionStrings section
var legacyRel = Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING")
              ?? builder.Configuration["APP__REL__CONNECTIONSTRING"];
if (!string.IsNullOrWhiteSpace(legacyRel))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = legacyRel;
}

var legacyVec = Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING")
              ?? builder.Configuration["APP__VEC__CONNECTIONSTRING"];
if (!string.IsNullOrWhiteSpace(legacyVec))
{
    builder.Configuration["ConnectionStrings:VEC"] = legacyVec;
}

var allowedOriginsSetting = builder.Configuration["ALLOWED_ORIGINS"]
    ?? Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");

string[]? configuredCorsOrigins = null;
if (!string.IsNullOrWhiteSpace(allowedOriginsSetting))
{
    configuredCorsOrigins = allowedOriginsSetting
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(origin => origin.Trim())
        .Where(origin => !string.IsNullOrWhiteSpace(origin) && origin != "*")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
              
var rel = builder.Configuration.GetConnectionString("DefaultConnection")
          ?? builder.Configuration["ConnectionStrings:DefaultConnection"]
          ?? builder.Configuration["APP__REL__CONNECTIONSTRING"]
          ?? Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING");

if (string.IsNullOrWhiteSpace(rel))
{
    throw new InvalidOperationException(
        "APP__REL__CONNECTIONSTRING is required for database connectivity. " +
        "Set it in appsettings.json, .env file, or environment variables. " +
        "Application cannot start without proper database configuration."
    );
}

var vec = builder.Configuration.GetConnectionString("VEC")
          ?? builder.Configuration["ConnectionStrings:VEC"]
          ?? builder.Configuration["APP__VEC__CONNECTIONSTRING"]
          ?? Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING");

if (string.IsNullOrWhiteSpace(vec))
{
    throw new InvalidOperationException(
        "APP__VEC__CONNECTIONSTRING is required for vector/AI database connectivity. " +
        "Set it in appsettings.json, .env file, or environment variables. " +
        "Application cannot start without proper database configuration."
    );
}

builder.Services.AddSingleton<VecConnResolver>();

// Helper to resolve VEC connection string (with fallback)
string Mask(string s) => Regex.Replace(s ?? "", @"Password=[^;]*", "Password=***");
Console.WriteLine("[Boot] REL = " + Mask(rel));
try { Console.WriteLine("[Boot] VEC = " + Mask(ResolveVecConn(builder.Configuration))); }
catch { Console.WriteLine("[Boot] VEC = <missing>"); }

// Forecasting services (Hybrid EMA/CMA approach)
builder.Services.AddScoped<HybridForecastService>();
builder.Services.AddScoped<ISqlCatalog, SqlCatalog>();
builder.Services.AddScoped<dataAccess.Forecasts.IForecastStore, dataAccess.Forecasts.ForecastStore>();

var possiblePaths = new[]
{
    Path.Combine(AppContext.BaseDirectory, "router.yaml"),                // runtime folder (EB = /var/app/current)
    Path.Combine(Directory.GetCurrentDirectory(), "router.yaml"),         // working dir fallback
    Path.Combine(Environment.CurrentDirectory, "router.yaml"),            // extra safety
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "router.yaml")    // another runtime base
};

string routerYamlPath = possiblePaths.FirstOrDefault(File.Exists)
    ?? throw new FileNotFoundException("router.yaml not found in any known location.", string.Join(", ", possiblePaths));

Console.WriteLine($"[Boot] Using router path: {routerYamlPath}");

var yamlContent = File.ReadAllText(routerYamlPath);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var routerCfg = deserializer.Deserialize<RouterConfig>(yamlContent) ?? new RouterConfig();
builder.Services.AddSingleton(routerCfg);
// YamlRouter removed - using ChatOrchestratorService instead
// builder.Services.AddSingleton<ITextRouter, YamlRouter>();

var reportModel = Environment.GetEnvironmentVariable("APP__REPORT__MODEL")
                 ?? builder.Configuration["APP:REPORT:MODEL"]
                 ?? "llama-3.3-70b-versatile";

var reportTemp = double.TryParse(
    Environment.GetEnvironmentVariable("APP__REPORT__TEMP") ?? builder.Configuration["APP:REPORT:TEMP"],
    out var t) ? t : 0.2;

var reportJson = bool.TryParse(
    Environment.GetEnvironmentVariable("APP__REPORT__JSON_MODE") ?? builder.Configuration["APP:REPORT:JSON_MODE"],
    out var jm) ? jm : true;

builder.Services.AddSingleton(new ReportGenOptions(reportModel, reportTemp, reportJson));

builder.Services.AddScoped<IGroqJsonClient>(sp =>
    new ModelSelectingGroqAdapter(
        sp.GetRequiredService<GroqJsonClient>(),
        sp.GetRequiredService<ReportGenOptions>()));

// Date parsing - LLM-based (replaces static DateRangeResolver)
builder.Services.AddScoped<ILlmDateParser, LlmDateParser>();
builder.Services.AddScoped<YamlPreprocessor>();

builder.Services.AddScoped<Func<string, CancellationToken, Task<string>>>(sp => async (specFile, ct) =>
{
    var spec = await ReportSpecLoader.LoadAsync(specFile, ct);
    return spec.Phase2System; // property from your ReportSpecLoader result
});
// Phase 4: Register IYamlReportRunner and IYamlIntentRunner interfaces
builder.Services.AddScoped<IYamlReportRunner, YamlReportRunner>();
builder.Services.AddScoped<YamlReportRunner>();

// Register YamlIntentRunner with intent classification config
builder.Services.AddScoped<IYamlIntentRunner>(sp =>
{
    var groq = sp.GetRequiredService<GroqJsonClient>();
    var exampleRetriever = sp.GetRequiredService<IntentExampleRetriever>();
    var logger = sp.GetRequiredService<ILogger<dataAccess.Reports.YamlIntentRunner>>();
    var routerConfig = sp.GetRequiredService<RouterConfig>();
    
    return new dataAccess.Reports.YamlIntentRunner(
        groq,
        exampleRetriever,
        logger,
        routerConfig.IntentClassification
    );
});

// Keep for backward compatibility
builder.Services.AddScoped<dataAccess.Reports.YamlIntentRunner>(sp =>
    (dataAccess.Reports.YamlIntentRunner)sp.GetRequiredService<IYamlIntentRunner>()
);

// ====================================================================
// RAG CLASSIFIER SERVICES (Phase 2 - Token Optimization)
// ====================================================================
// Embedding service for semantic similarity search (local ONNX inference)
builder.Services.AddSingleton<dataAccess.Services.IEmbeddingService, dataAccess.Services.LocalEmbeddingService>();

// Example retriever for RAG-based intent classification
builder.Services.AddSingleton<dataAccess.Services.IntentExampleRetriever>();

// In-memory JSON FAQ service for business rules RAG (replaces Vertex AI)
builder.Services.AddScoped<dataAccess.Services.IJsonFaqService, dataAccess.Services.JsonFaqService>();

// NOTE: YamlIntentRunner is already registered above.
// It will automatically receive IntentExampleRetriever via constructor injection.

// ====================================================================
// LOCAL DECODER SERVICE (Phase 3.5 - Groq API Refactor for Low-RAM Deployment)
// ====================================================================
// Local decoder service for generating natural language responses (chitchat/faq)
// REFACTORED: Now uses Groq API (llama-3.1-8b-instant) instead of local ONNX Phi-3
// This change enables deployment on low-RAM environments (~1GB) by removing heavy model inference
// NOTE: RAG Classifier (Phase 2) remains ONNX-based (all-MiniLM) as it's lightweight
builder.Services.AddScoped<dataAccess.Services.ILocalDecoderService, dataAccess.Services.LocalDecoderService>();

builder.Services.AddSingleton<IReportRunStore, ReportRunStore>();
builder.Services.AddSingleton<TimeResolver>();
builder.Services.AddSingleton<CapabilityGuard>();
builder.Services.AddSingleton<MetricMapper>();
builder.Services.AddSingleton<AnswerFormatter>();
builder.Services.AddScoped<INlqService, NlqService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your Supabase JWT token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSingleton(new PromptLoader());
builder.Services.AddSingleton(provider =>
{
    var loader = provider.GetRequiredService<PromptLoader>();
    return ConfigLoader.Load(loader, "config.yaml");   // loads identity, etc.
});
builder.Services.AddSingleton<PromptRegistry>();

builder.Services.AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["APP__EMBED__BASEADDRESS"]
                  ?? Environment.GetEnvironmentVariable("APP__EMBED__BASEADDRESS")
                  ?? "http://localhost:11434/";
    http.BaseAddress = new Uri(baseUrl);
});

// embeddingSync caller (typed HttpClient)
builder.Services.AddHttpClient("EmbeddingSync", (sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = Environment.GetEnvironmentVariable("APP__EMBEDDINGSYNC__BASEURL")
                 ?? cfg["APP__EMBEDDINGSYNC__BASEURL"]
                 ?? "http://localhost:57859"; // set to your embeddingSync port
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromMinutes(2);
});

// Registry/Planner/Executor
builder.Services.AddSingleton<Registry>(sp =>
{
    var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Planning", "SchemaRegistry.json"));
    return JsonSerializer.Deserialize<Registry>(json) ?? new Registry();
});

// ==============================================================================
// DATABASE CONTEXTS (Proper Separation)
// ==============================================================================
// AppDbContext: Business data (products, orders, expenses, suppliers, etc.)
// Uses REL (relational/business) database connection
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(rel)
       .UseSnakeCaseNamingConvention()
);

// AiDbContext: AI-related data (chat sessions, messages, feedback, FAQ logs)
// Uses VEC (vector/AI) database connection
builder.Services.AddDbContext<AiDbContext>(opt =>
    opt.UseNpgsql(vec)
       .UseSnakeCaseNamingConvention()
);

// ==============================================================================
// SEMANTIC KERNEL INTEGRATION (Day 1)
// ==============================================================================

// Get API keys from environment/config
var fastLlmApiKey = builder.Configuration["APP__SK__FAST_LLM__API_KEY"]
    ?? Environment.GetEnvironmentVariable("APP__SK__FAST_LLM__API_KEY");

var smartLlmApiKey = builder.Configuration["APP__SK__SMART_LLM__API_KEY"]
    ?? Environment.GetEnvironmentVariable("APP__SK__SMART_LLM__API_KEY");

if (!string.IsNullOrWhiteSpace(fastLlmApiKey) && !string.IsNullOrWhiteSpace(smartLlmApiKey))
{
    // Build Semantic Kernel with two LLM services
    var kernelBuilder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

    // Fast LLM for routing/classification (Llama 3.1 8B)
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: builder.Configuration["APP__SK__FAST_LLM__MODEL"] ?? "llama-3.1-8b-instant",
        apiKey: fastLlmApiKey,
        serviceId: "fast-llm",
        endpoint: new Uri(builder.Configuration["APP__SK__FAST_LLM__ENDPOINT"] ?? "https://api.groq.com/openai/v1")
    );

    // Smart LLM for SQL generation/analysis (Llama 3.3 70B)
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: builder.Configuration["APP__SK__SMART_LLM__MODEL"] ?? "llama-3.3-70b-versatile",
        apiKey: smartLlmApiKey,
        serviceId: "smart-llm",
        endpoint: new Uri(builder.Configuration["APP__SK__SMART_LLM__ENDPOINT"] ?? "https://api.groq.com/openai/v1")
    );

    var kernel = kernelBuilder.Build();

    // ✅ Import SK plugins from Plugins directory (Day 2)
    try
    {
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins");
        
        if (Directory.Exists(pluginsPath))
        {
            // Import all plugins from the Plugins directory
            // This will automatically discover skprompt.txt + config.json in subdirectories
            var orchestrationPath = Path.Combine(pluginsPath, "Orchestration");
            var databasePath = Path.Combine(pluginsPath, "Database");
            var analysisPath = Path.Combine(pluginsPath, "Analysis");
            var businessRulesPath = Path.Combine(pluginsPath, "BusinessRules");

            var pluginsLoaded = 0;

            if (Directory.Exists(orchestrationPath))
            {
                kernel.ImportPluginFromPromptDirectory(orchestrationPath, "Orchestration");
                pluginsLoaded++;
            }
            
            if (Directory.Exists(databasePath))
            {
                kernel.ImportPluginFromPromptDirectory(databasePath, "Database");
                pluginsLoaded++;
            }
            
            if (Directory.Exists(analysisPath))
            {
                kernel.ImportPluginFromPromptDirectory(analysisPath, "Analysis");
                pluginsLoaded++;
            }

            if (Directory.Exists(businessRulesPath))
            {
                kernel.ImportPluginFromPromptDirectory(businessRulesPath, "BusinessRules");
                pluginsLoaded++;
            }

            Console.WriteLine($"[SK] ✅ Plugins loaded from: {pluginsPath}");
            
            // Log all registered plugins and functions for debugging
            Console.WriteLine($"[SK] Registered plugins ({kernel.Plugins.Count}):");
            foreach (var plugin in kernel.Plugins)
            {
                Console.WriteLine($"[SK]   - Plugin: '{plugin.Name}' ({plugin.Count()} functions)");
                foreach (var function in plugin)
                {
                    Console.WriteLine($"[SK]     └─ Function: '{function.Name}' (Description: {function.Description ?? "N/A"})");
                }
            }

            if (pluginsLoaded == 0)
            {
                throw new InvalidOperationException("No plugins were loaded. Check Plugins directory structure.");
            }
        }
        else
        {
            Console.WriteLine($"[SK] ⚠️ Plugins directory not found: {pluginsPath}");
            throw new DirectoryNotFoundException($"Plugins directory not found: {pluginsPath}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SK] ❌ Failed to load plugins: {ex.Message}");
        Console.WriteLine($"[SK] Stack trace: {ex.StackTrace}");
        throw; // Fail startup if plugins cannot be loaded
    }

    builder.Services.AddSingleton(kernel);

    Console.WriteLine("[SK] Semantic Kernel initialized with multi-LLM strategy");
    Console.WriteLine("[SK] Fast LLM: llama-3.1-8b-instant (routing)");
    Console.WriteLine("[SK] Smart LLM: llama-3.3-70b-versatile (SQL/analysis)");
}
else
{
    Console.WriteLine("[SK] Semantic Kernel disabled - API keys not found");
}

// ✅ Day 2: LLM Orchestration Services
builder.Services.AddScoped<IDatabaseSchemaService, DatabaseSchemaService>();
builder.Services.AddScoped<IChatOrchestratorService, ChatOrchestratorService>();
builder.Services.AddSingleton<IPluginValidator, PluginValidator>();

// Telemetry Logger Service
builder.Services.AddScoped<TelemetryLogger>();

// Chat History Service (Phase 1: Slot-filling memory)
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddScoped<ChatHistoryService>(); // Keep for backward compatibility

// Phase 3: YAML-driven Runners with Slot Validation
builder.Services.AddScoped<IForecastRunnerService, ForecastRunnerService>();


builder.Services.AddHttpClient();

// Groq client (typed HttpClient) — MUST set BaseAddress
builder.Services.AddHttpClient<GroqJsonClient>((sp, http) =>
{
    http.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    http.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddScoped<VectorSearchService>();

// LLM SQL Generation services
builder.Services.AddSingleton<LlmSqlPromptLoader>();
builder.Services.AddScoped<LlmSqlGenerator>();
builder.Services.AddScoped<SqlValidator>();
builder.Services.AddScoped<ISafeSqlExecutor, SafeSqlExecutor>();
builder.Services.AddSingleton<VirtualTableRewriter>();

// Query Pipeline services
builder.Services.AddScoped<LlmSummarizer>(); // Still used by ChatOrchestratorService
// builder.Services.AddSingleton<ResponseFormatter>(); // Removed - not used
// builder.Services.AddScoped<QueryPipeline>(); // Removed - using ChatOrchestratorService instead

// CORS - Emergency Fix: Allow any origin for Vercel/HuggingFace deployment
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy =>
    {
        policy.AllowAnyOrigin()  // ⚠️ EMERGENCY FIX: Allows Vercel (or any host) to connect
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// JWT Authentication (Supabase)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var supabaseUrl = builder.Configuration["SUPABASE_URL"] 
            ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        var supabaseJwtSecret = builder.Configuration["SUPABASE_JWT_SECRET"]
            ?? Environment.GetEnvironmentVariable("SUPABASE_JWT_SECRET");

        // CRITICAL: Fail fast if JWT secret is not configured (security requirement)
        if (string.IsNullOrWhiteSpace(supabaseJwtSecret))
        {
            throw new InvalidOperationException(
                "SUPABASE_JWT_SECRET is required for JWT authentication. " +
                "Set it in appsettings.json, .env file, or environment variables. " +
                "Application cannot start without proper JWT configuration."
            );
        }

        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            throw new InvalidOperationException(
                "SUPABASE_URL is required for JWT authentication. " +
                "Set it in appsettings.json, .env file, or environment variables."
            );
        }

        // Enforce HTTPS metadata in production (security requirement)
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(supabaseJwtSecret)
            ),
            // Environment-specific validation: strict in production, flexible in development
            ValidateIssuer = !builder.Environment.IsDevelopment(),
            ValidIssuer = supabaseUrl,
            ValidateAudience = !builder.Environment.IsDevelopment(),
            // Accept multiple audiences for tighter security validation
            ValidAudiences = new[] { "authenticated", "api" },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1), // Reduced from 5 minutes for tighter security
            // Map Supabase 'sub' claim to NameIdentifier for proper User.Identity resolution
            NameClaimType = "sub"
        };

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                // SECURITY: Log authentication failures with source IP for monitoring/alerting
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var remoteIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var host = context.Request.Host.ToString();
                var path = context.Request.Path.ToString();
                
                logger.LogWarning(
                    "JWT authentication failed from {RemoteIp} for {Host}{Path}. Reason: {ErrorMessage}",
                    remoteIp,
                    host,
                    path,
                    context.Exception.Message
                );
                
                // Keep console log for backward compatibility
                Console.WriteLine($"[Auth] JWT validation failed: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var userId = context.Principal?.FindFirst("sub")?.Value;
                Console.WriteLine($"[Auth] JWT validated for user: {userId}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Define "ApiUser" policy for all authenticated API access
    options.AddPolicy("ApiUser", policy =>
    {
        policy.RequireAuthenticatedUser();
    });
});

// Rate Limiting (security requirement - prevent brute force and DoS)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0 // No queueing, reject immediately when limit exceeded
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ? Multi-tenancy: HttpContext access for SqlCatalog business_id extraction
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();
var app = builder.Build();

// ✅ Validate SK plugins on startup (Day 2)
if (!string.IsNullOrWhiteSpace(fastLlmApiKey) && !string.IsNullOrWhiteSpace(smartLlmApiKey))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IPluginValidator>();
        var validationResult = await validator.ValidateAllPluginsAsync();

        if (!validationResult.IsValid)
        {
            Console.WriteLine("[SK] ❌ Plugin validation failed:");
            foreach (var error in validationResult.Errors)
                Console.WriteLine($"  - {error}");
            
            if (app.Environment.IsProduction())
            {
                throw new InvalidOperationException("SK plugin validation failed in production. Cannot start application.");
            }
        }
        else
        {
            Console.WriteLine($"[SK] ✅ All {validationResult.ValidPlugins.Count} plugins validated successfully:");
            foreach (var plugin in validationResult.ValidPlugins)
                Console.WriteLine($"  - {plugin}");
        }

        if (validationResult.Warnings.Count > 0)
        {
            Console.WriteLine("[SK] ⚠️ Plugin warnings:");
            foreach (var warning in validationResult.Warnings)
                Console.WriteLine($"  - {warning}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SK] ❌ Plugin validation error: {ex.Message}");
        if (app.Environment.IsProduction())
            throw;
    }
}

// 1. Enable middleware to serve generated Swagger as a JSON endpoint.
app.UseSwagger();

// 2. Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.)
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BuiswAIz API V1");
    
    // 👇 OPTIONAL PERO RECOMMENDED:
    // Ito ang gagawin para pagbukas mo ng URL, Swagger agad ang bubungad (no need mag type ng /swagger)
    c.RoutePrefix = string.Empty; 
});

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        if (feature is null)
        {
            return;
        }

        var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("GlobalExceptionHandler");

        var exception = feature.Error;
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        logger.LogError(exception, "Unhandled exception {TraceId} at {RequestPath}", traceId, feature.Path);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        object payload = app.Environment.IsDevelopment()
            ? new { error = exception.GetType().Name, message = exception.Message, traceId }
            : new { error = "InternalServerError", traceId };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    });
});

app.UseCors("default");
app.UseRateLimiter(); // Apply rate limiting before authentication
app.UseAuthentication();
app.UseBusinessScoping(); // ✅ Extract user_id and business_id from JWT for multi-tenancy
app.UseAuthorization();
app.MapControllers();

// ✅ Day 2: SK Orchestration Test Endpoint
app.MapPost("/api/debug/sk-orchestrate", async (
    HttpContext httpContext,
    IChatOrchestratorService orchestrator,
    DebugSkRequest req,
    CancellationToken ct) =>
{
    try
    {
        // SECURITY: Extract userId from authenticated JWT token
        var userIdClaim = httpContext.User?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "Valid JWT authentication required for debug endpoint.",
                statusCode: 401
            );
        }

        // Extract business_id from middleware context for multi-tenancy scoping
        int? businessId = httpContext.Items.ContainsKey("BusinessId")
            ? httpContext.Items["BusinessId"] as int?
            : null;

        var result = await orchestrator.HandleQueryAsync(req.Query, userId, businessId, null, ct);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "SK Orchestration Error",
            detail: ex.Message,
            statusCode: 500
        );
    }
}).RequireAuthorization("ApiUser"); // Enforce JWT authentication with ApiUser policy

app.MapGet("/api/debug/groq-ping", async (GroqJsonClient groq, CancellationToken ct) =>
{
    // Instruct the model to return valid JSON immediately
    var system = """Return exactly {"pong":true}.""";
    var doc = await groq.CompleteJsonAsyncChat(system, "ping", null, 0.0, ct);
    return Results.Json(doc.RootElement);
});

app.MapGet("/api/debug/expense-queries", async (ISqlCatalog catalog, CancellationToken ct) =>
{
    try
    {
        var startStr = "2025-10-01";
        var endStr = "2025-10-31";
        
        var summary = await catalog.RunAsync("EXPENSE_SUMMARY", new Dictionary<string, object?> { ["start"] = startStr, ["end"] = endStr }, ct);
        var categories = await catalog.RunAsync("TOP_EXPENSE_CATEGORIES", new Dictionary<string, object?> { ["start"] = startStr, ["end"] = endStr, ["k"] = 5 }, ct);
        var daily = await catalog.RunAsync("EXPENSE_BY_DAY", new Dictionary<string, object?> { ["start"] = startStr, ["end"] = endStr }, ct);
        var recent = await catalog.RunAsync("EXPENSE_RECENT_TRANSACTIONS", new Dictionary<string, object?> { ["start"] = startStr, ["end"] = endStr, ["limit"] = 10 }, ct);

        return Results.Ok(new
        {
            summary,
            categories,
            daily,
            recent,
            debug_info = new { startStr, endStr, message = "Raw query results for October 2025" }
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message, stack = ex.StackTrace });
    }
});

// Debug endpoint to check raw expense data
app.MapGet("/api/debug/expense-data", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        var start = DateOnly.Parse("2025-10-01");
        var end = DateOnly.Parse("2025-10-31");
        
        // Get all expenses in the period with category info
        var expenses = await db.Expenses
            .Where(e => e.OccurredOn >= start && e.OccurredOn <= end)
            .Join(db.Categories, e => e.CategoryId!, c => c.Id, (e, c) => new {
                ExpenseId = e.Id,
                Amount = e.Amount,
                OccurredOn = e.OccurredOn,
                CategoryId = e.CategoryId,
                CategoryName = c.Name,
                Notes = e.Notes
            })
            .OrderByDescending(x => x.OccurredOn)
            .ToListAsync(ct);

        // Calculate totals by category
        var categoryTotals = expenses
            .GroupBy(x => x.CategoryName)
            .Select(g => new {
                category = g.Key,
                total_amount = g.Sum(x => x.Amount),
                transaction_count = g.Count(),
                transactions = g.Select(x => new {
                    id = x.ExpenseId,
                    amount = x.Amount,
                    date = x.OccurredOn,
                    notes = x.Notes
                }).ToList()
            })
            .OrderByDescending(x => x.total_amount)
            .ToList();

        return Results.Ok(new {
            period = new { start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") },
            total_expenses = expenses.Count,
            total_amount = expenses.Sum(x => x.Amount),
            category_breakdown = categoryTotals,
            all_transactions = expenses
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message, stack = ex.StackTrace });
    }
});

app.MapGet("/api/debug/expense-spec-deep", async () =>
{
    try
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts", "reports.expense.yaml");
        var text = await File.ReadAllTextAsync(path);

    app.MapGet("/api/debug/schema-mapping", (IDatabaseSchemaService schemaService, bool refresh = false) =>
    {
        var diagnostics = schemaService.GetDiagnostics(refresh);
        return Results.Ok(diagnostics);
    })
    .WithName("DebugSchemaMapping")
    .WithTags("Debug");
        // simple heuristics
        var hasTabs = text.Contains('\t');
        var beginsWithBom = text.Length > 0 && text[0] == '\uFEFF';
        var phase1Idx = text.IndexOf("phase1:");
        var sysIdx = text.IndexOf("system:", phase1Idx >= 0 ? phase1Idx : 0);

        // Take a short preview around phase1.system
        string preview = "";
        if (sysIdx >= 0)
        {
            var start = Math.Max(0, sysIdx - 40);
            var len = Math.Min(text.Length - start, 260);
            preview = text.Substring(start, len);
        }

        // try to parse via loader to capture the precise exception
        try
        {
            var spec = await dataAccess.Api.Services.ReportSpecLoader.LoadAsync("reports.expense.yaml", CancellationToken.None);
            return Results.Ok(new
            {
                ok = true,
                path,
                length = text.Length,
                hasTabs,
                beginsWithBom,
                phase1_len = spec.Phase1System?.Length ?? 0,
                phase2_len = spec.Phase2System?.Length ?? 0,
                preview
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                ok = false,
                path,
                length = text.Length,
                hasTabs,
                beginsWithBom,
                preview,
                ex = ex.GetType().FullName,
                ex_message = ex.Message
            });
        }
    }
    catch (Exception exOuter)
    {
        return Results.Ok(new { ok = false, ex = exOuter.GetType().FullName, ex_message = exOuter.Message });
    }
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// Debug endpoint to test LLM SQL generation
app.MapPost("/api/debug/llm-sql", async (
    HttpContext ctx,
    LlmSqlGenerator sqlGen,
    SqlValidator validator,
    SafeSqlExecutor executor,
    VirtualTableRewriter rewriter,
    CancellationToken ct) =>
{
    var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken: ct);
    var question = body?.GetValueOrDefault("question") ?? "";
    
    if (string.IsNullOrWhiteSpace(question))
        return Results.BadRequest(new { error = "Question is required" });

    try
    {
        // Generate SQL
        var sql = await sqlGen.GenerateSqlAsync(question, ct);
        
        if (string.IsNullOrWhiteSpace(sql))
            return Results.Ok(new { success = false, message = "Could not generate SQL" });

        // Rewrite virtual tables (if any)
        sql = rewriter.RewriteIfNeeded(sql);

        // Validate SQL
        var (isValid, errorMsg) = validator.ValidateSql(sql);
        
        if (!isValid)
            return Results.Ok(new { success = false, sql, error = errorMsg, validated = false });

        // Ensure LIMIT
        sql = validator.EnsureLimit(sql, 50);

        // Execute SQL
        var results = await executor.ExecuteQueryAsync(sql, ct);
        var markdown = executor.FormatAsMarkdown(results);

        return Results.Ok(new
        {
            success = true,
            question,
            sql,
            validated = true,
            rowCount = results.Count,
            results,
            markdown
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, error = ex.Message, stack = ex.StackTrace });
    }
});

app.MapGet("/api/reports/expense/by-id/{id:guid}", async (
    Guid id,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var connStr = ResolveVecConn(cfg);

    await using var conn = new NpgsqlConnection(connStr);
    await conn.OpenAsync(ct);

    // MULTI-TENANCY: Extract business_id from HttpContext
    int? businessId = ctx.Items.TryGetValue("BusinessId", out var bidObj) && bidObj is int bid ? bid : (int?)null;
    Console.WriteLine($"[MULTI-TENANCY] /api/reports/expense/by-id/{id} | BusinessId: {businessId?.ToString() ?? "NULL"}");

    // Build SQL with business_id filter for security
    var hasBusinessFilter = businessId.HasValue;
    
    var sql = hasBusinessFilter
        ? @"select ui_spec
            from public.reports
            where id = @id AND business_id = @business_id
            limit 1;"
        : @"select ui_spec
            from public.reports
            where id = @id
            limit 1;";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    if (hasBusinessFilter)
        cmd.Parameters.AddWithValue("business_id", businessId!.Value);

    var uiSpecJson = (string?)await cmd.ExecuteScalarAsync(ct);
    if (uiSpecJson is null)
        return Results.NotFound(new { error = "NotFound", id });

    return Results.Json(new { ui_spec = JsonDocument.Parse(uiSpecJson).RootElement, id });
});

app.MapGet("/api/reports/recent", async (
    HttpContext ctx,
    CancellationToken ct) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var connStr = ResolveVecConn(cfg);

    // ✅ Read optional domain & limit
    var domain = ctx.Request.Query["domain"].ToString()?.Trim().ToLowerInvariant();
    var limitQ = ctx.Request.Query["limit"].ToString();
    var limit = int.TryParse(limitQ, out var n) && n > 0 && n <= 10 ? n : 5;

    await using var conn = new NpgsqlConnection(connStr);
    await conn.OpenAsync(ct);

    // MULTI-TENANCY: Extract business_id from HttpContext (use business_id instead of user_id)
    int? businessId = ctx.Items.TryGetValue("BusinessId", out var bidObj) && bidObj is int bid ? bid : (int?)null;
    Console.WriteLine($"[MULTI-TENANCY] /api/reports/recent | BusinessId: {businessId?.ToString() ?? "NULL"}");

    // Check if business filtering should be applied
    var hasBusinessFilter = businessId.HasValue;

    // Build SQL with business_id filtering (not user_id)
    string sql;
    if (string.IsNullOrWhiteSpace(domain) || domain == "all")
    {
        sql = hasBusinessFilter
            ? @"select id, domain, period_label, created_at, ui_spec
                from public.reports
                where business_id = @business_id
                order by created_at desc
                limit @limit;"
            : @"select id, domain, period_label, created_at, ui_spec
                from public.reports
                order by created_at desc
                limit @limit;";
    }
    else
    {
        sql = hasBusinessFilter
            ? @"select id, domain, period_label, created_at, ui_spec
                from public.reports
                where domain = @domain AND business_id = @business_id
                order by created_at desc
                limit @limit;"
            : @"select id, domain, period_label, created_at, ui_spec
                from public.reports
                where domain = @domain
                order by created_at desc
                limit @limit;";
    }

    await using var cmd = new NpgsqlCommand(sql, conn);
    if (!string.IsNullOrWhiteSpace(domain) && domain != "all")
        cmd.Parameters.AddWithValue("domain", domain);
    if (hasBusinessFilter)
        cmd.Parameters.AddWithValue("business_id", businessId!.Value);
    cmd.Parameters.AddWithValue("limit", limit);

    var list = new List<object>();
    await using var rdr = await cmd.ExecuteReaderAsync(ct);
    while (await rdr.ReadAsync(ct))
    {
        var id = rdr.GetGuid(0);
        var dm = rdr.GetString(1);
        var periodLabel = rdr.IsDBNull(2) ? null : rdr.GetString(2);
        var createdAt = (DateTimeOffset)rdr.GetFieldValue<DateTime>(3);
        var uiSpecJson = rdr.GetString(4);

        list.Add(new
        {
            id,
            domain = dm,
            period_label = periodLabel,
            created_at = createdAt,
            ui_spec = JsonDocument.Parse(uiSpecJson).RootElement
        });
    }

    return Results.Json(list);
});

// === Popup: load full report by id ===
app.MapGet("/api/reports/sales/by-id/{id:guid}", async (
    Guid id,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var connStr = ResolveVecConn(cfg);

    await using var conn = new NpgsqlConnection(connStr);
    await conn.OpenAsync(ct);

    // MULTI-TENANCY: Extract business_id from HttpContext
    int? businessId = ctx.Items.TryGetValue("BusinessId", out var bidObj) && bidObj is int bid ? bid : (int?)null;
    Console.WriteLine($"[MULTI-TENANCY] /api/reports/sales/by-id/{id} | BusinessId: {businessId?.ToString() ?? "NULL"}");

    // Build SQL with business_id filter for security
    var hasBusinessFilter = businessId.HasValue;
    
    var sql = hasBusinessFilter
        ? @"select ui_spec
            from public.reports
            where id = @id AND business_id = @business_id
            limit 1;"
        : @"select ui_spec
            from public.reports
            where id = @id
            limit 1;";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    if (hasBusinessFilter)
        cmd.Parameters.AddWithValue("business_id", businessId!.Value);

    var uiSpecJson = (string?)await cmd.ExecuteScalarAsync(ct);
    if (uiSpecJson is null)
        return Results.NotFound(new { error = "NotFound", id });

    return Results.Json(new { ui_spec = JsonDocument.Parse(uiSpecJson).RootElement, id });
});

app.MapGet("/api/debug/db-ping", async (IConfiguration cfg, CancellationToken ct) =>
{
    async Task<object> Try(string name, string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return new { name, ok = false, error = "no-conn" };
        try
        {
            var b = new Npgsql.NpgsqlConnectionStringBuilder(cs) { Timeout = 3, CommandTimeout = 3 };
            await using var c = new Npgsql.NpgsqlConnection(b.ConnectionString);
            await c.OpenAsync(ct);
            return new { name, ok = true, host = b.Host, ssl = b.SslMode.ToString() };
        }
        catch (Exception ex)
        {
            return new { name, ok = false, error = ex.GetType().Name, message = ex.Message };
        }
    }

    var cfgV = cfg["APP__VEC__CONNECTIONSTRING"] ?? cfg["APP:VEC:CONNECTIONSTRING"] ?? cfg.GetConnectionString("VEC") ?? cfg.GetConnectionString("Vector");
    var cfgR = cfg["APP__REL__CONNECTIONSTRING"] ?? cfg["APP:REL:CONNECTIONSTRING"] ?? cfg.GetConnectionString("REL");

    return Results.Json(new
    {
        rel = await Try("REL", cfgR),
        vec = await Try("VEC", cfgV)
    });
});


// ❌ DELETED: Legacy endpoints (2025-12-15)
// app.MapNlqEndpoint(); // Depends on deleted NlqEndpoint
// app.MapQueryPipelineEndpoint(); // Depends on deleted QueryPipeline
// Replacement: Use POST /api/chat/query (ChatOrchestratorService)
// Assuming you have: public sealed record AssistantRequest(string Text, string? Domain);
// ---------- DELETED: Unused helper functions (2025-12-27) ----------


static string ResolveVecConn(IConfiguration cfg)
{
    return cfg["APP__VEC__CONNECTIONSTRING"]
        ?? cfg["APP:VEC:CONNECTIONSTRING"]
        ?? cfg.GetConnectionString("VEC")
        ?? cfg.GetConnectionString("Vector")
        ?? cfg.GetConnectionString("APP__VEC__CONNECTIONSTRING")
        ?? throw new InvalidOperationException("Vector connection string not found (APP__VEC__CONNECTIONSTRING / ConnectionStrings:VEC/Vector).");
}

// ----------------------------------------------------------------

// -------------------------------
// Forecast endpoints (Sales / Expenses) — UI-spec outputs
// -------------------------------
// POST /api/forecasts/{domain}/generate
// Body: { period: {start,end,label?}, kpis: { horizon_days }, forecast: <json> }
app.MapPost("/api/forecasts/{domain}/generate", async (
    string domain,
    HttpContext ctx,
    dataAccess.Forecasts.IForecastStore store,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(ctx.Request.Body, System.Text.Encoding.UTF8);
    var raw = await reader.ReadToEndAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    var root = doc.RootElement;

    // normalize domain
    var dom = (domain ?? "").ToLowerInvariant();
    if (dom != "sales" && dom != "expenses") dom = "expenses";

    // REQUIRED by your schema
    int horizon = 30;
    if (root.TryGetProperty("kpis", out var k) && k.TryGetProperty("horizon_days", out var hz) && hz.TryGetInt32(out var hd))
        horizon = Math.Max(1, hd);

    // pack period → params jsonb
    var @params = new System.Text.Json.Nodes.JsonObject();
    if (root.TryGetProperty("period", out var p))
    {
        if (p.TryGetProperty("start", out var ps) && ps.ValueKind == System.Text.Json.JsonValueKind.String) @params["start"] = ps.GetString();
        if (p.TryGetProperty("end", out var pe) && pe.ValueKind == System.Text.Json.JsonValueKind.String) @params["end"] = pe.GetString();
        if (p.TryGetProperty("label", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.String) @params["label"] = pl.GetString();
    }

    // forecast result payload
    var resultNode = System.Text.Json.Nodes.JsonNode.Parse(
        root.TryGetProperty("forecast", out var fc) ? fc.GetRawText() : "{}"
    ) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();

    var id = await store.SaveAsync(
        domain: dom,
        target: "overall",
        horizonDays: horizon,
        @params: @params,
        result: resultNode,
        status: "done",
        ct: ct
    );

    return Results.Json(new { ok = true, id });
});

// GET /api/forecasts/by-id/{id}
app.MapGet("/api/forecasts/by-id/{id:guid}", async (
    Guid id,
    dataAccess.Forecasts.IForecastStore store,
    CancellationToken ct) =>
{
    var row = await store.GetAsync(id, ct);
    return row is null ? Results.NotFound() : Results.Json(row);
});

// GET /api/forecasts/recent?domain=expenses&limit=5
app.MapGet("/api/forecasts/recent", async (
    HttpContext ctx,
    string? domain,
    int? limit,
    dataAccess.Forecasts.IForecastStore store,
    CancellationToken ct) =>
{
    var dom = (domain ?? "expenses").ToLowerInvariant();
    if (dom != "sales" && dom != "expenses") dom = "expenses";
    var lim = limit.GetValueOrDefault(5);

    // MULTI-TENANCY: Extract businessId from HttpContext
    int? businessId = ctx.Items.TryGetValue("BusinessId", out var bidObj) && bidObj is int bid ? bid : (int?)null;
    Guid userId = ctx.Items.TryGetValue("UserId", out var uidObj) && uidObj is Guid uid ? uid : Guid.Empty;
    Console.WriteLine("[MULTI-TENANCY] /api/forecasts/recent | BusinessId: " + (businessId?.ToString() ?? "NULL"));

    // Call multi-tenancy version
    var rows = await store.RecentAsync(userId, businessId, dom, lim, ct);
    return Results.Json(rows);
});

app.MapPost("/api/debug/forecasts/insert-one", async (
    IConfiguration cfg,
    CancellationToken ct) =>
{
    try
    {
        var vec = new VecConnResolver(cfg).Resolve(); // ← SAME resolution as ForecastStore
        await using var conn = new Npgsql.NpgsqlConnection(vec);
        await conn.OpenAsync(ct);

        const string sql = @"
            insert into public.forecasts (domain, target, horizon_days, params, status, result)
            values ('expenses', 'overall', 7, '{}'::jsonb, 'done', null)
            returning id;";
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        var id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        return Results.Json(new { ok = true, id });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            ok = false,
            where = "insert",
            error = ex.GetType().Name,
            message = ex.Message
        }, statusCode: 500);
    }
});

app.MapGet("/api/debug/config/vec-all", (IConfiguration cfg) =>
{
    string Get(string k) => string.IsNullOrWhiteSpace(cfg[k]) ? "<null>" : cfg[k]!;
    return Results.Json(new
    {
        APP__VEC__CONNECTIONSTRING = Get("APP__VEC__CONNECTIONSTRING"),
        APP_VEC_CONNECTIONSTRING_COLON = Get("APP:VEC:CONNECTIONSTRING"),
        ConnStr_VEC = cfg.GetConnectionString("VEC") ?? "<null>",
        ConnStr_Vector = cfg.GetConnectionString("Vector") ?? "<null>"
    });
});

app.Run();

public sealed class RouteReq { public string? Input { get; set; } }
public sealed record AssistantRequest(string Text, string? Domain);
public sealed record DebugSkRequest(string Query);

// -------------------------------
// Helpers: sync trigger & debounce
// -------------------------------
public static class SyncHelper
{
    // Safer empty content for POST endpoints that expect a body
    public static readonly StringContent EmptyJson = new("", Encoding.UTF8, "application/json");

    // Debounce state (shared across requests)
    private static DateTime _lastSyncUtc = DateTime.MinValue;
    private static readonly object _syncLock = new();

    public static bool ShouldRunSync(TimeSpan minInterval)
    {
        lock (_syncLock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastSyncUtc < minInterval) return false;
            _lastSyncUtc = now;
            return true;
        }
    }
}

