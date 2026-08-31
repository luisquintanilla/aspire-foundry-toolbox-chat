namespace FoundryToolbox.Client;

public sealed class FoundryToolboxClientOptions
{
    public required Uri Endpoint { get; init; }

    public string? AdditionalFeatures { get; init; }

    public int MaximumDiscoveryAttempts { get; init; } = 3;

    public TimeSpan InitialDiscoveryRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ConnectionDisposalTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal FoundryToolboxEndpointInfo Validate()
    {
        var endpointInfo = FoundryToolboxEndpoint.Parse(Endpoint);

        if (MaximumDiscoveryAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDiscoveryAttempts),
                "Maximum discovery attempts must be between 1 and 10.");
        }

        if (InitialDiscoveryRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialDiscoveryRetryDelay),
                "The initial discovery retry delay cannot be negative.");
        }

        if (DiscoveryTimeout <= TimeSpan.Zero ||
            DiscoveryTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(DiscoveryTimeout),
                "The discovery timeout must be greater than zero and no longer than 10 minutes.");
        }

        if (ConnectionDisposalTimeout <= TimeSpan.Zero ||
            ConnectionDisposalTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConnectionDisposalTimeout),
                "The connection disposal timeout must be greater than zero and no longer than one minute.");
        }

        return endpointInfo;
    }
}
