using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using DallasPermitFunc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var tenantAId = builder.Configuration["API_TENANT_ID"]
    ?? throw new InvalidOperationException("API_TENANT_ID is not configured.");
var apiClientId = builder.Configuration["API_CLIENT_ID"]
    ?? throw new InvalidOperationException("API_CLIENT_ID is not configured.");
var foundryEndpoint = builder.Configuration["FOUNDRY_PROJECT_ENDPOINT"]
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not configured.");
var foundryScope = builder.Configuration["FOUNDRY_SCOPE"] ?? "https://ai.azure.com/.default";

// 1. IN-CODE VALIDATION: commercial Entra (Tenant A) JWT validated against its JWKS — no Easy Auth.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{tenantAId}/v2.0";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            // v1 tokens (accessTokenAcceptedVersion null/1) use the sts.windows.net issuer.
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{tenantAId}/v2.0",
                $"https://sts.windows.net/{tenantAId}/"
            ],
            ValidateAudience = true,
            ValidAudiences = [apiClientId, $"api://{apiClientId}"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

builder.UseMiddleware<JwtAuthenticationMiddleware>();

// 2. FOUNDRY CLIENT: Tenant B managed identity (commercial or Azure Government, driven by FOUNDRY_SCOPE).
builder.Services.AddSingleton(_ => new AIProjectClient(
    new Uri(foundryEndpoint),
    new ScopedCredential(new DefaultAzureCredential(), foundryScope)));

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
