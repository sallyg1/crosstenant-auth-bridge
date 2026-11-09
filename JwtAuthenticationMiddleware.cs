using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace DallasPermitFunc;

public sealed class JwtAuthenticationMiddleware(ILogger<JwtAuthenticationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            await next(context);
            return;
        }

        var result = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded)
        {
            logger.LogWarning("JWT validation failed for {Function}: {Reason}",
                context.FunctionDefinition.Name, result.Failure?.Message ?? "no bearer token");
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            context.GetInvocationResult().Value = new UnauthorizedResult();
            return;
        }

        httpContext.User = result.Principal;
        await next(context);
    }
}
