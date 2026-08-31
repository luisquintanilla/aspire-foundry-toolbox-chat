namespace AspireFoundryToolboxChat.Web.Services.Ingestion;

internal static class LocalDataPath
{
    internal static string GetVectorStorePath(
        string baseDirectory,
        string localApplicationDataDirectory,
        bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var dataDirectory = baseDirectory;
        if (isWindows && baseDirectory.StartsWith(@"\\", StringComparison.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
            dataDirectory = Path.Combine(
                localApplicationDataDirectory,
                "AspireFoundryToolboxChat");
        }

        return Path.Combine(dataDirectory, "vector-store.db");
    }
}
