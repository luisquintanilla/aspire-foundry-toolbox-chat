using Azure.Core;

namespace Aspire.Foundry.Tests;

internal sealed class TestTokenCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            new AccessToken(token, DateTimeOffset.MaxValue));
}
