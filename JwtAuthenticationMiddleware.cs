using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DallasPermitFunc;

public sealed class JwtAuthenticationMiddleware(
    IConfiguration config,
    ILogger<JwtAuthenticationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private const string LegacyScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";
    private const string LegacyOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private readonly string _requiredScope = string.IsNullOrWhiteSpace(config["API_REQUIRED_SCOPE"])
        ? "access_as_user"
        : config["API_REQUIRED_SCOPE"]!.Trim();

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            await next(context);
            return;
        }

        var result = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            logger.LogWarning("JWT validation failed for {Function}: {Reason}",
                context.FunctionDefinition.Name, result.Failure?.Message ?? "no bearer token");
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            context.GetInvocationResult().Value = new UnauthorizedResult();
            return;
        }

        var oid = result.Principal.FindFirst("oid")?.Value
            ?? result.Principal.FindFirst(LegacyOidClaimType)?.Value;
        var idtyp = result.Principal.FindFirst("idtyp")?.Value;

        if (string.IsNullOrWhiteSpace(oid) || string.Equals(idtyp, "app", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("JWT rejected for {Function}: missing user oid or app-only token (idtyp: {IdTyp})",
                context.FunctionDefinition.Name, idtyp ?? "none");
            httpContext.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            context.GetInvocationResult().Value = new ObjectResult(new { error = "User identity (oid) is required." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        var hasRequiredScope = result.Principal.Claims
            .Where(c => c.Type == "scp" || c.Type == LegacyScopeClaimType)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(_requiredScope, StringComparer.Ordinal);

        if (!hasRequiredScope)
        {
            logger.LogWarning("JWT missing required scope '{Scope}' for {Function} (oid: {Oid})",
                _requiredScope, context.FunctionDefinition.Name, oid);
            httpContext.Response.Headers.WWWAuthenticate =
                $"Bearer error=\"insufficient_scope\", scope=\"{_requiredScope}\"";
            context.GetInvocationResult().Value = new ObjectResult(new { error = "Insufficient scope." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        httpContext.User = result.Principal;
        await next(context);
    }
}
