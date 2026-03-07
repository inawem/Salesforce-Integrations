using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.ApplicationInsights.Extensibility;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using SalesforceIntegration.Data;
using SalesforceIntegration.Services;
using SalesforceIntegration.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// CONFIGURATION & SECRETS
// ============================================================================

// Load from Key Vault in production
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    var secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
    builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
}

// ============================================================================
// DATABASE CONFIGURATION
// ============================================================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(30);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelaySeconds: 10, errorNumbersToAdd: null);
    })
);

// ============================================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================================

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var auth0Domain = builder.Configuration["Auth0:Domain"];
        var auth0Audience = builder.Configuration["Auth0:Audience"];

        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            NameClaimType = "https://salesforce-integration/name",
            RoleClaimType = "https://salesforce-integration/roles"
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError("Authentication failed: {Error}", context.Exception?.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("read", policy => policy.RequireClaim("scope", "read:customers"));
    options.AddPolicy("write", policy => policy.RequireClaim("scope", "write:customers"));
});

// ============================================================================
// DEPENDENCY INJECTION
// ============================================================================

// Salesforce Configuration
var sfConfig = new SalesforceConfig
{
    ClientId = builder.Configuration["Salesforce:ClientId"],
    ClientSecret = builder.Configuration["Salesforce:ClientSecret"],
    InstanceUrl = builder.Configuration["Salesforce:InstanceUrl"]
};
builder.Services.AddSingleton(sfConfig);

// HTTP Clients with resilience
builder.Services
    .AddHttpClient<ISalesforceClient, SalesforceClient>()
    .ConfigureHttpClient(client => { client.Timeout = TimeSpan.FromSeconds(30); })
    .AddPolicyHandler(HttpClientPolicies.GetRetryPolicy())
    .AddPolicyHandler(HttpClientPolicies.GetRateLimitPolicy());

builder.Services
    .AddHttpClient<IAuth0Client, Auth0Client>()
    .ConfigureHttpClient(client => { client.Timeout = TimeSpan.FromSeconds(15); })
    .AddPolicyHandler(HttpClientPolicies.GetRetryPolicy());

// Services
builder.Services
    .AddScoped<ICustomerSyncService, CustomerSyncService>()
    .AddScoped<ICustomerRepository, CustomerRepository>()
    .AddScoped<ISyncCheckpointRepository, SyncCheckpointRepository>()
    .AddScoped<ISyncFailureRepository, SyncFailureRepository>()
    .AddScoped<IServiceBusPublisher, ServiceBusPublisher>()
    .AddScoped<IAuth0Client, Auth0Client>();

// ============================================================================
// LOGGING & MONITORING
// ============================================================================

builder.Services.AddApplicationInsightsTelemetry();
builder.Logging
    .ClearProviders()
    .AddConsole()
    .AddApplicationInsights();

builder.Services.AddLogging(config =>
{
    config.SetMinimumLevel(LogLevel.Information);
    if (builder.Environment.IsDevelopment())
    {
        config.SetMinimumLevel(LogLevel.Debug);
    }
});

// ============================================================================
// API & MIDDLEWARE
// ============================================================================

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", builder =>
    {
        builder
            .WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                builder.Configuration["Frontend:Url"] ?? ""
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Salesforce Integration API",
        Version = "v1",
        Description = "Enterprise Salesforce CRM sync API with Azure integration"
    });

    // Add JWT auth to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme"
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
            new string[] { }
        }
    });
});

// ============================================================================
// BUILD & RUN
// ============================================================================

var app = builder.Build();

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngular");

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Database migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Database migrations completed successfully");
}

var portOrDefault = builder.Configuration["PORT"] ?? "5000";
app.Urls.Add($"http://0.0.0.0:{portOrDefault}");

app.Run();
