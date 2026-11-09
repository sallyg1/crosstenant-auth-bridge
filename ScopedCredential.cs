using Azure.Core;

namespace DallasPermitFunc;

// Foundry SDK hardcodes the commercial audience (ai.azure.com); this forces the configured one, e.g. ai.azure.us for Azure Government.
public sealed class ScopedCredential(TokenCredential inner, string scope) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => inner.GetToken(WithScope(requestContext), cancellationToken);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => inner.GetTokenAsync(WithScope(requestContext), cancellationToken);

    private TokenRequestContext WithScope(TokenRequestContext ctx)
        => new([scope], ctx.ParentRequestId, ctx.Claims, ctx.TenantId, ctx.IsCaeEnabled);
}
