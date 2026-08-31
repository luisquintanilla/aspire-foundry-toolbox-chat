using Azure.Core;

namespace FoundryToolbox.Client.Tests;

internal sealed class TestTokenCredential(string token) : TokenCredential
{
    public string[] LastScopes { get; private set; } = [];

    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        LastScopes = requestContext.Scopes;
        return new AccessToken(token, DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        LastScopes = requestContext.Scopes;
        return ValueTask.FromResult(
            new AccessToken(token, DateTimeOffset.MaxValue));
    }
}
