using dataAccess.Api;
// ❌ REMOVED: using dataAccess.Api.Endpoints; (AssistantEndpoint deleted)
using dataAccess.Api.Services;
using dataAccess.Services;
using dataAccess.Planning;
using dataAccess.Planning.Nlq;
// ❌ REMOVED: using dataAccess.Planning.Validation; (PlanValidator deleted)
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

// Forecasting services (Hybrid EMA/CMA is primary; SimpleForecast kept for backward compatibility)
builder.Services.AddScoped<HybridForecastService>();
builder.Services.AddScoped<SimpleForecastService>();
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
builder.Services.AddSingleton<dataAccess.Services.IJsonFaqService, dataAccess.Services.JsonFaqService>();

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
// ❌ ZOMBIE SERVICE - Deleted 2025-12-15
// builder.Services.AddSingleton<PromptComposer>();

// Embedder (typed HttpClient)
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

// ==============================================================================
// EXISTING SERVICES
// ==============================================================================

// ❌ ZOMBIE SERVICES - Deleted 2025-12-15
// builder.Services.AddScoped<PlannerService>();
// builder.Services.AddScoped<PlanValidator>();
// builder.Services.AddScoped<PlanExecutor>();
builder.Services.AddHttpClient();

// Groq client (typed HttpClient) — MUST set BaseAddress
builder.Services.AddHttpClient<GroqJsonClient>((sp, http) =>
{
    http.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    http.Timeout = TimeSpan.FromSeconds(60);
});

// Query services
// ❌ ZOMBIE SERVICES - Deleted 2025-12-15
// builder.Services.AddScoped<SqlQueryService>();
// builder.Services.AddScoped<HybridQueryService>();
builder.Services.AddScoped<VectorSearchService>(); // ✅ Keep - used by utilities

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

static double? SafePct(double prev, double cur)
{
    if (double.IsNaN(prev) || prev == 0) return null;
    return (cur - prev) / prev * 100.0;
}

static string NewRunId() => $"r_sales_{Guid.NewGuid():N}".ToLowerInvariant();

// ⚠️ DEPRECATION LAYER - Task 2.1 (Block 1, Hour 2-3)
// Middleware to mark legacy endpoints with deprecation headers
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
    
    // List of deprecated endpoints that bypass ChatOrchestratorService
    var legacyEndpoints = new Dictionary<string, string>
    {
        { "/api/nlq", "Natural Language Query (bypasses orchestrator)" },
        { "/api/sql/products", "Product list (direct SQL access)" },
        { "/api/sql/suppliers", "Supplier list (direct SQL access)" },
        { "/api/sql/productcategory", "Category list (direct SQL access)" },
        { "/api/sql/route", "SQL routing (direct SQL execution - CRITICAL RISK)" },
        { "/api/hybrid/route", "Hybrid query (SQL+Vector without orchestrator)" },
        { "/api/vector/route", "Vector search (bypasses orchestrator)" },
        { "/api/assistant", "Legacy assistant (replaced by /api/chat/query)" }
    };
    
    var matchedEndpoint = legacyEndpoints.Keys.FirstOrDefault(endpoint => path.StartsWith(endpoint));
    
    if (matchedEndpoint != null)
    {
        // Log deprecation warning
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DeprecationMiddleware");
        logger.LogWarning(
            "⚠️ DEPRECATED ENDPOINT ACCESSED: {Path} - {Description}. Use /api/chat/query instead.",
            matchedEndpoint,
            legacyEndpoints[matchedEndpoint]
        );
        
        // Add deprecation headers (will be added after endpoint processes)
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Deprecated"] = "true";
            context.Response.Headers["X-Replacement"] = "/api/chat/query";
            context.Response.Headers["X-Sunset-Date"] = "2025-12-31";
            context.Response.Headers["X-Deprecation-Info"] = legacyEndpoints[matchedEndpoint];
            return Task.CompletedTask;
        });
    }
    
    await next(context);
});

app.UseCors("default");
app.UseRateLimiter(); // Apply rate limiting before authentication
app.UseAuthentication();
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

        var result = await orchestrator.HandleQueryAsync(req.Query, userId, null, ct);
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

// -------------------------------
// ❌ LEGACY SQL ENDPOINTS DELETED (Zombie Services)
// Replacement: Use POST /api/chat/query
// -------------------------------

// ❌ DELETED: All legacy SQL/Hybrid/Vector route endpoints (see above comment)

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

// ❌ DELETED: POST /api/reports/inventory/plan (orphaned - depends on deleted PlannerService)
// Replacement: Use POST /api/chat/query with intent "reports.inventory"

// ❌ DELETED: POST /api/reports/inventory/render (orphaned - depends on deleted PlanValidator)
// Replacement: Use POST /api/chat/query with YamlReportRunner (165 lines removed)

// ❌ DELETED: POST /api/reports/expense/generate (orphaned - depends on deleted PlannerService)
// Replacement: Use POST /api/chat/query with intent "reports.expenses"

app.MapGet("/api/reports/expense/by-id/{id:guid}", async (
    Guid id,
    HttpContext ctx,
    CancellationToken ct) =>
{
    var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var connStr = ResolveVecConn(cfg);

    await using var conn = new NpgsqlConnection(connStr);
    await conn.OpenAsync(ct);

    const string sql = @"
        select ui_spec
        from public.reports
        where id = @id
        limit 1;";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);

    var uiSpecJson = (string?)await cmd.ExecuteScalarAsync(ct);
    if (uiSpecJson is null)
        return Results.NotFound(new { error = "NotFound", id });

    return Results.Json(new { ui_spec = JsonDocument.Parse(uiSpecJson).RootElement, id });
});

// ❌ DELETED: POST /api/reports/sales/generate (orphaned - depends on deleted PlannerService)
// Replacement: Use POST /api/chat/query with intent "reports.sales"

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

    // ✅ If no domain or "all" → include every report
    string sql;
    if (string.IsNullOrWhiteSpace(domain) || domain == "all")
    {
        sql = @"
            select id, domain, period_label, created_at, ui_spec
            from public.reports
            order by created_at desc
            limit @limit;";
    }
    else
    {
        sql = @"
            select id, domain, period_label, created_at, ui_spec
            from public.reports
            where domain = @domain
            order by created_at desc
            limit @limit;";
    }

    await using var cmd = new NpgsqlCommand(sql, conn);
    if (!string.IsNullOrWhiteSpace(domain) && domain != "all")
        cmd.Parameters.AddWithValue("domain", domain);
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

    const string sql = @"
        select ui_spec
        from public.reports
        where id = @id
        limit 1;";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);

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
// ---------- tiny helpers (can be placed above the map) ----------
static string NormalizeReportDomain(string? domain)
{
    if (string.IsNullOrWhiteSpace(domain)) return "sales";
    var d = domain.Trim().ToLowerInvariant();
    return d switch
    {
        "expense" or "expenses" => "expenses",
        "inventories" => "inventory",
        "sale" => "sales",
        _ => d
    };
}

static ForecastDomain ToForecastDomain(string? domain)
{
    var d = (domain ?? "").Trim().ToLowerInvariant();
    return (d == "expenses" || d == "expense")
        ? ForecastDomain.Expenses
        : ForecastDomain.Sales;
}
static string ResolveVecConn(IConfiguration cfg)
{
    return cfg["APP__VEC__CONNECTIONSTRING"]
        ?? cfg["APP:VEC:CONNECTIONSTRING"]
        ?? cfg.GetConnectionString("VEC")
        ?? cfg.GetConnectionString("Vector")
        ?? cfg.GetConnectionString("APP__VEC__CONNECTIONSTRING")
        ?? throw new InvalidOperationException("Vector connection string not found (APP__VEC__CONNECTIONSTRING / ConnectionStrings:VEC/Vector).");
}

static string BuildPeriodLabel(DateOnly start, DateOnly end)
{
    var sameYear = start.Year == end.Year;
    var left = start.ToDateTime(TimeOnly.MinValue);
    var right = end.ToDateTime(TimeOnly.MinValue);
    var L = left.ToString("MMM d", CultureInfo.InvariantCulture);
    var R = sameYear
        ? right.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
        : right.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    return $"{L}–{R}";
}

static (DateOnly start, DateOnly end, string label, int days) ResolvePeriod(JsonElement root)
{
    // Accepts either explicit start/end or horizon "days"
    var period = root.TryGetProperty("period", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
    var label = period.ValueKind == JsonValueKind.Object && period.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
        ? (l.GetString() ?? "")
        : null;

    DateOnly start, end;
    if (period.ValueKind == JsonValueKind.Object &&
        period.TryGetProperty("start", out var ps) && ps.ValueKind == JsonValueKind.String &&
        period.TryGetProperty("end", out var pe) && pe.ValueKind == JsonValueKind.String &&
        DateOnly.TryParse(ps.GetString(), out start) && DateOnly.TryParse(pe.GetString(), out end))
    {
        var computed = string.IsNullOrWhiteSpace(label) ? BuildPeriodLabel(start, end) : label!;
        return (start, end, computed, (end.DayNumber - start.DayNumber) + 1);
    }

    // Fallback: horizon "days" from body or default 30
    var days = root.TryGetProperty("days", out var dEl) && dEl.TryGetInt32(out var dVal) && dVal > 0 && dVal <= 60 ? dVal : 30;
    var today = DateOnly.FromDateTime(DateTime.UtcNow); // or use PH time if preferred
    start = today;
    end = today.AddDays(days - 1);
    return (start, end, BuildPeriodLabel(start, end), days);
}

static object SafeArray(JsonElement el)
{
    if (el.ValueKind == JsonValueKind.Array)
    {
        return System.Text.Json.Nodes.JsonNode.Parse(el.GetRawText())!; // independent JsonNode/JsonArray
    }
    return System.Text.Json.Nodes.JsonNode.Parse("[]")!;
}

static (decimal? sumForecast, decimal? last7, decimal? last28, JsonElement actual, JsonElement forecast)
    LiftForecastFields(JsonElement payload)
{
    decimal? GetNum(string name)
    {
        // Try top-level first
        if (payload.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String && Decimal.TryParse(el.GetString(), out var s)) return s;
        }
        
        // Try inside "kpis" object
        if (payload.TryGetProperty("kpis", out var kpis) && kpis.ValueKind == JsonValueKind.Object)
        {
            if (kpis.TryGetProperty(name, out var kpiEl))
            {
                if (kpiEl.ValueKind == JsonValueKind.Number && kpiEl.TryGetDecimal(out var d2)) return d2;
                if (kpiEl.ValueKind == JsonValueKind.String && Decimal.TryParse(kpiEl.GetString(), out var s2)) return s2;
            }
        }
        
        return null;
    }

    JsonElement FindSeries(string key1, string key2, out bool found)
    {
        // supports { series:{ history:[...], forecast:[...] } } 
        // OR { series:{ actual:[...], forecast:[...] } } 
        // OR top-level arrays
        if (payload.TryGetProperty("series", out var sObj) && sObj.ValueKind == JsonValueKind.Object)
        {
            if (sObj.TryGetProperty(key1, out var s1) && s1.ValueKind == JsonValueKind.Array) 
            { 
                found = true; 
                return s1; 
            }
            if (sObj.TryGetProperty(key2, out var s2) && s2.ValueKind == JsonValueKind.Array) 
            { 
                found = true; 
                return s2; 
            }
        }
        if (payload.TryGetProperty(key2, out var s3) && s3.ValueKind == JsonValueKind.Array) 
        { 
            found = true; 
            return s3; 
        }
        found = false; 
        return default;
    }

    var sumF = GetNum("sum_forecast");
    var a7 = GetNum("last_7d_actual");
    var a28 = GetNum("last_28d_actual");

    var _ = false;
    var actual = FindSeries("history", "actual", out _);  // Try "history" first, then "actual"
    var forecast = FindSeries("forecast", "forecast", out _);

    return (sumF, a7, a28, actual, forecast);
}

// --- Forecast narrative (analyst vibe, single paragraph, no bullets) ---
static async Task<string[]> GenerateAnalystNarrativeAsync(
    GroqJsonClient groq,
    string domainTitle,
    string periodLabel,
    decimal? sumForecast,
    decimal? last7,
    decimal? last28,
    JsonElement historicalData,
    JsonElement forecastData,
    CancellationToken ct)
{
    static string PickStyleHint(int seed)
    {
        string[] styles =
        {
            "analyst memo; 3–5 sentences; crisp, specific; no bullets; avoid clichés",
            "neutral research note; short, declarative sentences; no list formatting",
            "executive brief; 3–4 sentences; mention one concrete driver; no hype",
            "data-first commentary; weave KPIs into prose; forbid boilerplate phrasing"
        };
        return styles[Math.Abs(seed) % styles.Length];
    }

    var styleHint = PickStyleHint((periodLabel ?? "").GetHashCode() + DateTime.UtcNow.DayOfYear);

    var system = """
    You are a business analyst. Write a short, SINGLE-PARAGRAPH explanation of the forecast.
    Tone: professional analyst. No bullets, no headings, no emojis.
    Use 3–5 sentences. Be concrete and period-specific. Reference the provided data.
    Do NOT invent numbers, dates, or categories beyond the input.
    
    IMPORTANT: You will receive:
    1. Historical Data: Past actual values (dates + amounts) from recent days
    2. Forecast Data: Predicted future values (dates + amounts) for the forecast period
    3. KPIs: Aggregated summaries
    
    Your task:
    - Analyze the historical trend from the data points
    - Compare the forecast predictions to recent historical performance
    - Identify any patterns (increasing/decreasing/stable)
    - Be specific with numbers and dates when relevant
    
    DO NOT say "lack of data" or "data is null" - you have both historical and forecast arrays.
    
    Return STRICT JSON: {"narrative":"<one paragraph>"} and nothing else.
    """;

    // Build historical summary from data points
    string historicalSummary = "No historical data";
    if (historicalData.ValueKind == JsonValueKind.Array && historicalData.GetArrayLength() > 0)
    {
        var count = historicalData.GetArrayLength();
        var recentPoints = new List<string>();
        for (int i = Math.Max(0, count - 5); i < count; i++)
        {
            var point = historicalData[i];
            if (point.TryGetProperty("date", out var d) && point.TryGetProperty("value", out var v))
            {
                var dateStr = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
                var val = v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
                recentPoints.Add($"{dateStr}: ₱{val:N2}");
            }
        }
        historicalSummary = recentPoints.Count > 0 
            ? $"Last {recentPoints.Count} days: {string.Join(", ", recentPoints)}"
            : "Historical data available but no recent points";
    }

    // Build forecast summary from data points
    string forecastSummary = "No forecast data";
    if (forecastData.ValueKind == JsonValueKind.Array && forecastData.GetArrayLength() > 0)
    {
        var count = forecastData.GetArrayLength();
        var forecastPoints = new List<string>();
        for (int i = 0; i < Math.Min(5, count); i++)
        {
            var point = forecastData[i];
            if (point.TryGetProperty("date", out var d) && point.TryGetProperty("value", out var v))
            {
                var dateStr = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
                var val = v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
                forecastPoints.Add($"{dateStr}: ₱{val:N2}");
            }
        }
        forecastSummary = forecastPoints.Count > 0 
            ? $"Next {forecastPoints.Count} days: {string.Join(", ", forecastPoints)}"
            : "Forecast data available but no points";
    }

    // Safe array length checks
    int histCount = historicalData.ValueKind == JsonValueKind.Array ? historicalData.GetArrayLength() : 0;
    int foreCount = forecastData.ValueKind == JsonValueKind.Array ? forecastData.GetArrayLength() : 0;

    var user = $"""
    Domain: {domainTitle}
    Forecast Period: {periodLabel}

    Historical Data ({histCount} days):
    {historicalSummary}

    Forecast Predictions ({foreCount} days):
    {forecastSummary}

    Aggregated KPIs:
      - Forecasted Total: {(sumForecast is null || sumForecast == 0 ? "₱0.00" : "₱" + sumForecast.Value.ToString("N2"))}
      - Recent 7d Actual: {(last7 is null || last7 == 0 ? "₱0.00" : "₱" + last7.Value.ToString("N2"))}
      - Recent 28d Actual: {(last28 is null || last28 == 0 ? "₱0.00" : "₱" + last28.Value.ToString("N2"))}

    Task: Write a brief forecast analysis based on the historical trend and predictions shown above.
    Style hint: {styleHint}
    Constraints:
      - Single paragraph only.
      - No bullet points or line breaks.
      - Reference specific data points or patterns you observe.
    """;

    try
    {
        // Use your existing overload (no GroqJsonRequest type)
    using var doc = await groq.CompleteJsonAsyncReport(system: system, user: user, data: null, temperature: 0.0, ct: ct);

        if (doc.RootElement.TryGetProperty("narrative", out var n) && n.ValueKind == JsonValueKind.String)
        {
            var text = (n.GetString() ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return new[] { text }; // UI expects array
        }
    }
    catch
    {
        // fall through to rotating fallback
    }

    string[] fallbacks =
    {
        $"For {periodLabel}, projected totals track close to the recent run-rate: momentum from the last 28 days sets the baseline, while the most recent week contributes only a modest pull on the average. Variability appears contained, so the outlook is steady unless an atypical spike arrives mid-cycle.",
        $"The forecast for {periodLabel} reflects a continuation of recent behavior, with day-to-day swings narrowing versus prior weeks. Results from the last 7 and 28 days anchor the baseline, implying limited drift unless demand shifts meaningfully outside recent ranges.",
        $"Across {periodLabel}, expected totals align with short- and medium-term signals. The 28-day profile defines the pace and the latest 7-day print offers a light near-term steer, suggesting a stable path absent unusual promotions or shocks."
    };
    return new[] { fallbacks[Math.Abs((periodLabel ?? "").GetHashCode() + DateTime.UtcNow.DayOfYear) % fallbacks.Length] };
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
    string? domain,
    int? limit,
    dataAccess.Forecasts.IForecastStore store,
    CancellationToken ct) =>
{
    var dom = (domain ?? "expenses").ToLowerInvariant();
    if (dom != "sales" && dom != "expenses") dom = "expenses";

    var lim = limit.GetValueOrDefault(5);

    // ✅ correct parameter order: (string domain, int limit, CancellationToken ct)
    var rows = await store.RecentAsync(dom, lim, ct);
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

app.MapPost("/api/assistant", async (
    HttpContext ctx,
    dataAccess.Reports.YamlIntentRunner intentRunner,  // ← LLM-based routing using router.intent.yaml
    dataAccess.Reports.YamlReportRunner yamlRunner,
    HybridForecastService forecastSvc,
    dataAccess.Forecasts.IForecastStore forecastStore,
    GroqJsonClient groq,
    CancellationToken ct) =>
{
    try
    {
        // 0) Parse request
        var req = await ctx.Request.ReadFromJsonAsync<AssistantRequest>(cancellationToken: ct);
        if (req is null || string.IsNullOrWhiteSpace(req.Text))
            return Results.Json(new { error = "Text is required." }, statusCode: 400);

    var userText = req.Text;
    var userLower = userText.ToLowerInvariant();

    // 1) INTENT/DOMAIN via LLM classifier (router.intent.yaml)
    string intent; string? domain; double conf;

    if (string.IsNullOrWhiteSpace(req.Domain))
    {
        try
        {
            // Use YamlIntentRunner for LLM-based routing (no history for minimal endpoint)
            using var doc = await intentRunner.RunIntentAsync(userText, null, ct);
            var root = doc.RootElement;

            intent = root.TryGetProperty("intent", out var iEl) && iEl.ValueKind == JsonValueKind.String
                ? iEl.GetString() ?? ""
                : "";

            domain = root.TryGetProperty("domain", out var dEl) && dEl.ValueKind == JsonValueKind.String
                ? dEl.GetString()
                : null;

            conf = root.TryGetProperty("confidence", out var cEl) && cEl.TryGetDouble(out var cVal)
                ? Math.Clamp(cVal, 0.0, 1.0)
                : 0.5;

            if (string.IsNullOrWhiteSpace(intent))
                intent = "nlq";

            Console.WriteLine($"[Router:LLM] Intent: {intent}, Domain: {domain ?? "null"}, Confidence: {conf:F2}, Query: '{userText}'");

            // tiny domain inference only when needed for forecasting
            if (intent.Equals("forecasting", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(domain))
            {
                domain =
                    (userLower.Contains("gastos") || userLower.Contains("expense") || userLower.Contains("expenses") || userLower.Contains("spend"))
                        ? "expenses"
                        : ((userLower.Contains("sales") || userLower.Contains("revenue") || userLower.Contains("benta") || userLower.Contains("kita"))
                            ? "sales"
                            : "sales");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Router:LLM] Error: {ex.Message}");
            intent = "nlq"; domain = null; conf = 0.5;
        }
    }
    else
    {
        // Explicit domain in request → report
        intent = "report";
        domain = req.Domain!.ToLowerInvariant();
        conf = 1.0;
    }

    // ═══════════════════════════════════════════════════════════════
    // 1.5) OUT_OF_SCOPE - Handle non-business questions
    // ═══════════════════════════════════════════════════════════════
    if (intent.Equals("out_of_scope", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"[OutOfScope] Rejecting non-business query: '{userText}'");
        
        return Results.Json(new
        {
            mode = "chitchat",
            uiSpec = new
            {
                render = new 
                { 
                    kind = "markdown", 
                    content = "Sorry, I can't help you with that. I'm focused on helping with business and BuiswAIz-related questions.\n\n" +
                             "Try asking about:\n" +
                             "• Sales forecasting and predictions\n" +
                             "• Inventory management\n" +
                             "• Financial reports and analytics\n" +
                             "• Budget planning and tracking\n" +
                             "• Expense analysis"
                }
            },
            router = new { intent = "out_of_scope", domain, confidence = conf, rejected = true }
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // 2) FAQ - Handled by ChatOrchestrator (LocalDecoderService + Groq)
    // ═══════════════════════════════════════════════════════════════
    if (intent.Equals("faq", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var localDecoder = ctx.RequestServices.GetRequiredService<ILocalDecoderService>();
            var chatHistory = ctx.RequestServices.GetRequiredService<IChatHistoryService>();
            
            // Get recent chat history for conversational context
            var history = await chatHistory.GetRecentMessagesAsync(Guid.NewGuid(), limit: 5);
            
            // Call LocalDecoderService with "faq" intent
            var responseText = await localDecoder.GetResponseAsync(userText, history, "faq");
            
            return Results.Json(new
            {
                mode = "faq",
                uiSpec = new
                {
                    render = new { kind = "markdown", content = responseText }
                },
                router = new { intent = "faq", domain, confidence = conf }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAQ ERROR] {ex.Message}");
            // Fallback response
            return Results.Json(new
            {
                mode = "faq",
                uiSpec = new
                {
                    render = new 
                    { 
                        kind = "markdown", 
                        content = "I can help you with sales reports, expense tracking, inventory management, and forecasting. What would you like to know?"
                    }
                },
                router = new { intent = "faq", domain, confidence = conf }
            });
        }
    }

    // 3) FORECASTING
    if (intent.Equals("forecasting", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            // ✅ normalize for forecasting (expense/expenses both OK)
            var domainEnum = ToForecastDomain(domain);

            // horizon
            int days = 30;
            if (userLower.Contains("next week"))
                days = 7;
            else if (userLower.Contains("next month"))
            {
                var today = DateTime.Today;
                var firstOfNext = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                days = DateTime.DaysInMonth(firstOfNext.Year, firstOfNext.Month);
            }
            else
            {
                var m = Regex.Match(userLower, @"\b(\d+)\s*days?\b");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed))
                    days = Math.Clamp(parsed, 1, 60);
            }

            // 1) Compute numbers using Hybrid EMA/CMA forecasting
            var payload = await forecastSvc.ForecastAsync(
                domainEnum, 
                days, 
                emaAlpha: 0.2,      // Default EMA smoothing factor
                blendBeta: 0.7,     // Default blend weight (70% EMA, 30% CMA)
                from: null, 
                to: null, 
                ct);

        // 2) Turn payload into JsonElement so we can lift KPIs/period
        using var tmp = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var payloadEl = tmp.RootElement;

        // Helper in Program.cs:
        // static (decimal? sumForecast, decimal? last7, decimal? last28, JsonElement actual, JsonElement forecast)
        //     LiftForecastFields(JsonElement payload)
        var (sumF, last7, last28, _actual, _forecast) = LiftForecastFields(payloadEl);

        // Period label + domain title for the prompt
        var periodLabel = payloadEl.TryGetProperty("period", out var per) &&
                          per.TryGetProperty("label", out var lbl) && lbl.ValueKind == JsonValueKind.String
                            ? (lbl.GetString() ?? "")
                            : "";
        var domainTitle = domainEnum == dataAccess.Services.ForecastDomain.Expenses ? "Expenses" : "Sales";

        // 3) Ask Groq for a concise analyst narrative (helper also in Program.cs)
        // Task<string[]> GenerateAnalystNarrativeAsync(
        //     GroqJsonClient groq, string domainTitle, string periodLabel,
        //     decimal? sumForecast, decimal? last7, decimal? last28, 
        //     JsonElement historicalData, JsonElement forecastData, CancellationToken ct)
        var narrativeArr = await GenerateAnalystNarrativeAsync(
            groq, domainTitle, periodLabel, sumF, last7, last28, _actual, _forecast, ct);
        var narrative = (narrativeArr?.Length ?? 0) > 0 ? (narrativeArr![0] ?? "") : "";

        // 4) Merge: keep current payload shape, just add notes.narrative
        var uiNode = (JsonNode.Parse(payloadEl.GetRawText()) as JsonObject)
                        ?? new JsonObject();
        uiNode["notes"] = new JsonObject { ["narrative"] = narrative };

        // 5) Save forecast to database
        try
        {
            var paramsObj = new JsonObject();
            if (payloadEl.TryGetProperty("period", out var periodProp))
            {
                if (periodProp.TryGetProperty("start", out var startProp) && startProp.ValueKind == JsonValueKind.String)
                    paramsObj["start"] = startProp.GetString();
                if (periodProp.TryGetProperty("end", out var endProp) && endProp.ValueKind == JsonValueKind.String)
                    paramsObj["end"] = endProp.GetString();
                if (periodProp.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
                    paramsObj["label"] = labelProp.GetString();
            }

            // Store the full UI spec as the result
            var resultObj = (JsonNode.Parse(uiNode.ToJsonString()) as JsonObject) ?? new JsonObject();

            await forecastStore.SaveAsync(
                domain: domainEnum.ToString().ToLowerInvariant(),
                target: "overall",
                horizonDays: days,
                @params: paramsObj,
                result: resultObj,
                status: "done",
                ct: ct
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to save forecast to database: {ex.Message}");
            // Continue without throwing - don't break the user experience
        }

        // 6) Return (mode=forecast) so the UI path remains unchanged
        return Results.Json(new
        {
            mode = "forecast",
            domain = domainEnum.ToString().ToLowerInvariant(),
            uiSpec = uiNode,
            router = new { intent, domain = (domain ?? "sales"), confidence = conf }
        });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FORECAST ERROR] {ex.Message}");
            // Fallback to FAQ or chitchat if forecasting fails
            intent = "faq";
        }
    }

    // 3) REPORT (YAML)
    if (intent.Equals("report", StringComparison.OrdinalIgnoreCase))
    {
        // ✅ normalize for reports (expenses → expense to match filename)
        var chosenDomain = NormalizeReportDomain(domain);
        
        // Phase 3: Use YAML-driven slot-filling (no hardcoded fallbacks)
        // The yamlRunner will handle slot validation and return clarification prompts if needed
        var ui = await yamlRunner.RunAsync(chosenDomain, userText, ct);

        return Results.Json(new
        {
            mode = "report",
            domain = chosenDomain, // return normalized
            uiSpec = ui,
            router = new { intent, domain = chosenDomain, confidence = conf }
        });
    }

    // 4) NLQ → Try LLM SQL (primary), fallback to classic NLQ if needed
    if (intent.Equals("nlq", StringComparison.OrdinalIgnoreCase))
    {
        string markdown = "";
        string summary = "";
        bool llmSqlSuccess = false;
        string? usedMethod = null;
        try
        {
            var sqlGen = ctx.RequestServices.GetRequiredService<LlmSqlGenerator>();
            var validator = ctx.RequestServices.GetRequiredService<SqlValidator>();
            var executor = ctx.RequestServices.GetRequiredService<SafeSqlExecutor>();
            var summarizer = ctx.RequestServices.GetRequiredService<LlmSummarizer>();

            // Generate SQL using LLM
            var generatedSql = await sqlGen.GenerateSqlAsync(userText, ct);

            if (!string.IsNullOrWhiteSpace(generatedSql))
            {
                // Validate the SQL
                var (isValid, errorMsg) = validator.ValidateSql(generatedSql);

                if (isValid)
                {
                    // Ensure reasonable LIMIT
                    generatedSql = validator.EnsureLimit(generatedSql, 100);

                    // Execute the query
                    var results = await executor.ExecuteQueryAsync(generatedSql, ct);

                    // Remove columns with all null/empty values
                    if (results is IEnumerable<IDictionary<string, object?>> rowsList)
                    {
                        var rowsArr = rowsList.Select(r => new Dictionary<string, object?>(r)).ToList();
                        if (rowsArr.Count > 0)
                        {
                            var allKeys = rowsArr.SelectMany(r => r.Keys).Distinct().ToList();
                            var keysToKeep = allKeys.Where(k => rowsArr.Any(r => r.TryGetValue(k, out var v) && v != null && !(v is string s && string.IsNullOrWhiteSpace(s)))).ToList();
                            foreach (var row in rowsArr)
                            {
                                var keysToRemove = row.Keys.Except(keysToKeep).ToList();
                                foreach (var k in keysToRemove)
                                    row.Remove(k);
                            }
                            // Use filtered rows for markdown
                            results = rowsArr;
                        }
                    }

                    // Always serialize results to JSON then parse as JsonElement
                    var resultsJson = JsonSerializer.Serialize(results);
                    using var doc = JsonDocument.Parse(resultsJson);
                    var resultsElement = doc.RootElement;
                    int rowCount = (resultsElement.ValueKind == JsonValueKind.Array) ? resultsElement.GetArrayLength() : 0;
                    summary = await summarizer.SummarizeAsync(userText, generatedSql, resultsElement, rowCount, ct);

                    // Format as markdown
                    markdown = executor.FormatAsMarkdown(results, null);
                    llmSqlSuccess = true;
                    usedMethod = "llm_sql";
                }
                else
                {
                    Console.WriteLine($"[LLM SQL] Validation failed: {errorMsg}");
                }
            }
            else
            {
                Console.WriteLine("[LLM SQL] No SQL generated");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM SQL] Failed: {ex.Message}");
        }

        // FALLBACK: If LLM SQL failed, try classic NLQ endpoint
        if (!llmSqlSuccess)
        {
            try
            {
                var http = new HttpClient
                {
                    BaseAddress = new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host}")
                };

                var resp = await http.PostAsJsonAsync("/api/nlq", new { text = userText }, ct);
                
                if (resp.IsSuccessStatusCode)
                {
                    markdown = await resp.Content.ReadAsStringAsync(ct);
                    
                    // Check if NLQ returned a meaningful result
                    if (!string.IsNullOrWhiteSpace(markdown) && 
                        !markdown.Contains("I don't understand") && 
                        !markdown.Contains("I cannot") &&
                        markdown.Length > 20)
                    {
                        usedMethod = "classic_nlq";
                    }
                    else
                    {
                        markdown = "I'm not sure how to answer that question. Could you rephrase it or ask about specific business data?";
                        usedMethod = "none";
                    }
                }
                else
                {
                    markdown = "I encountered an error trying to answer your question. Please try rephrasing it.";
                    usedMethod = "none";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NLQ Fallback] Failed: {ex.Message}");
                markdown = "I encountered an error trying to answer your question. Please try rephrasing it.";
                usedMethod = "none";
            }
        }

        // Compose the response: summary (if any) + markdown table
        string combinedContent = string.IsNullOrWhiteSpace(summary)
            ? markdown
            : string.IsNullOrWhiteSpace(markdown)
                ? summary
                : $"{summary}\n\n{markdown}";

        // Remove markdown tables (lines starting with | or containing --- for table headers)
        string RemoveMarkdownTables(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var lines = input.Split('\n');
            var filtered = new List<string>();
            bool inTable = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Start of table: line starts with | and next line contains ---
                if (trimmed.StartsWith("|") && trimmed.Contains("|"))
                {
                    inTable = true;
                    continue;
                }
                if (inTable && (trimmed.Contains("---") || trimmed.StartsWith("|")))
                {
                    continue;
                }
                // End table if line is not a table line
                if (inTable && !trimmed.StartsWith("|"))
                {
                    inTable = false;
                }
                if (!inTable && !trimmed.StartsWith("|"))
                {
                    filtered.Add(line);
                }
            }
            return string.Join("\n", filtered).Trim();
        }

        var filteredContent = RemoveMarkdownTables(combinedContent);

        return Results.Json(new
        {
            mode = "nlq",
            uiSpec = new
            {
                render = new { kind = "markdown", content = filteredContent },
                // No suggested actions for NLQ - it's just data display
            },
            router = new { intent, domain, confidence = conf, method = usedMethod }
        });
    }


    // 5) CHITCHAT (prompt file)
    if (intent.Equals("chitchat", StringComparison.OrdinalIgnoreCase))

    {
        object? ui = null;
        var full = Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts", "chitchat.yaml");
        try
        {
            var yaml = File.ReadAllText(full);
            var des = new DeserializerBuilder().Build();
            var yamlObj = des.Deserialize<dynamic>(yaml);
            string systemPrompt = yamlObj["system"] ?? "You are a helpful assistant.";
            double temperature = 0.3;
            try
            {
                var tempObj = yamlObj["defaults"]?["model"]?["temperature"];
                if (tempObj != null)
                    temperature = Convert.ToDouble(tempObj);
            }
            catch { }

            using var doc = await groq.CompleteJsonAsyncChat(systemPrompt, req.Text, null, temperature, ct);
            ui = JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
        }
        catch (Exception ex)
        {
            ui = new
            {
                render = new { kind = "markdown", content = "Hello! 👋 How can I help you today?" },
                __debug = new { hint = "chitchat fallback", tried = full, error = ex.Message }
            };
        }

        return Results.Json(new
        {
            mode = "chitchat",
            uiSpec = ui,
            router = new { intent, domain, confidence = conf }
        });
    }

    // 6) Final fallback (should rarely hit)
    return Results.Json(new
    {
        mode = "chat",
        markdown = "Hi! How can I help?",
        router = new { intent = "chitchat", domain, confidence = conf }
    });
    }
    catch (Exception ex)
    {
        var errorId = Guid.NewGuid();
        Console.WriteLine($"[/api/assistant] FATAL ERROR {errorId}: {ex}");
        
        return Results.Json(new
        {
            mode = "error",
            error = $"⚠️ An internal server error occurred. Error ID: {errorId}",
            errorId = errorId.ToString(),
            details = ex.Message,
            uiSpec = new
            {
                render = new
                {
                    kind = "markdown",
                    content = $"⚠️ **An error occurred while processing your request.**\n\n" +
                             $"Error ID: `{errorId}`\n\n" +
                             $"Please try again or contact support with the Error ID if the problem persists."
                }
            }
        }, statusCode: 500);
    }
}).RequireAuthorization("ApiUser"); // Enforce JWT authentication with ApiUser policy

// ⚠️ STARTUP WARNING - Task 2.2 (Block 1, Hour 2-3)
// Display deprecated endpoints warning on every application start
Console.WriteLine();
Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                    ⚠️  DEPRECATED ENDPOINTS WARNING  ⚠️                    ║");
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("The following 8 endpoints bypass ChatOrchestratorService and are DEPRECATED:");
Console.WriteLine();
Console.WriteLine("  🔴 CRITICAL RISK:");
Console.WriteLine("     • POST /api/sql/route          - Direct SQL execution (security risk)");
Console.WriteLine("     • POST /api/hybrid/route       - SQL+Vector without validation");
Console.WriteLine();
Console.WriteLine("  🟠 HIGH RISK:");
Console.WriteLine("     • POST /api/nlq                - Natural language query (no validation)");
Console.WriteLine("     • GET  /api/sql/products       - Direct database access");
Console.WriteLine("     • GET  /api/sql/suppliers      - Direct database access");
Console.WriteLine("     • GET  /api/sql/productcategory - Direct database access");
Console.WriteLine();
Console.WriteLine("  🟡 MEDIUM RISK:");
Console.WriteLine("     • POST /api/vector/route       - Vector search bypass");
Console.WriteLine("     • POST /api/assistant          - Legacy YAML routing");
Console.WriteLine();
Console.WriteLine("  📅 SUNSET DATE: December 31, 2025");
Console.WriteLine("  ✅ REPLACEMENT: POST /api/chat/query (unified orchestrator)");
Console.WriteLine();
Console.WriteLine("  📊 All requests to deprecated endpoints will:");
Console.WriteLine("     - Return X-Deprecated: true header");
Console.WriteLine("     - Log warning messages");
Console.WriteLine("     - Continue functioning (soft deprecation)");
Console.WriteLine();
Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

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

    public static async Task RunEmbeddingSyncAllAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            _ = await http.PostAsync("/api/backfill/products", EmptyJson, ct);
            _ = await http.PostAsync("/api/backfill/suppliers", EmptyJson, ct);
            _ = await http.PostAsync("/api/backfill/categories", EmptyJson, ct);
        }
        catch (Exception ex)
        {
            // Minimal logging; replace with ILogger if preferred
            Console.Error.WriteLine($"[embeddingSync] backfill failed: {ex.Message}");
        }
    }
}

// Put this in Program.cs bottom region OR separate file in dataAccess.Reports namespace