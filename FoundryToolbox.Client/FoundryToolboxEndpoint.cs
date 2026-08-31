using System.Text.RegularExpressions;

namespace FoundryToolbox.Client;

public static partial class FoundryToolboxEndpoint
{
    public const string ApiVersion = "v1";

    [GeneratedRegex(
        @"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SafeSegmentRegex();

    public static Uri Build(
        Uri projectEndpoint,
        string toolboxName,
        string? version = null)
    {
        ValidateProjectEndpoint(projectEndpoint);
        ValidateSegment(toolboxName, "Toolbox name", nameof(toolboxName));

        if (version is not null)
        {
            ValidateSegment(version, "Toolbox version", nameof(version));
        }

        var projectPath = projectEndpoint.AbsolutePath.TrimEnd('/');
        var toolboxPath = version is null
            ? $"{projectPath}/toolboxes/{toolboxName}/mcp"
            : $"{projectPath}/toolboxes/{toolboxName}/versions/{version}/mcp";
        var builder = new UriBuilder(projectEndpoint)
        {
            Path = toolboxPath,
            Query = $"api-version={ApiVersion}",
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    public static FoundryToolboxEndpointInfo Parse(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "The Toolbox endpoint must be an absolute HTTPS URI without user information or a fragment.",
                nameof(endpoint));
        }

        if (!string.Equals(
                endpoint.Query,
                $"?api-version={ApiVersion}",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The Toolbox endpoint query must be exactly 'api-version={ApiVersion}'.",
                nameof(endpoint));
        }

        var segments = endpoint.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        var toolboxIndex = Array.FindLastIndex(
            segments,
            segment => string.Equals(segment, "toolboxes", StringComparison.Ordinal));

        var consumerEndpoint =
            toolboxIndex > 0 &&
            segments.Length == toolboxIndex + 3 &&
            string.Equals(segments[^1], "mcp", StringComparison.Ordinal);
        var pinnedEndpoint =
            toolboxIndex > 0 &&
            segments.Length == toolboxIndex + 5 &&
            string.Equals(segments[toolboxIndex + 2], "versions", StringComparison.Ordinal) &&
            string.Equals(segments[^1], "mcp", StringComparison.Ordinal);

        if (!consumerEndpoint && !pinnedEndpoint)
        {
            throw new ArgumentException(
                "The Toolbox endpoint path must end in '/toolboxes/{name}/mcp' or '/toolboxes/{name}/versions/{version}/mcp'.",
                nameof(endpoint));
        }

        var toolboxName = segments[toolboxIndex + 1];
        ValidateSegment(toolboxName, "Toolbox name", nameof(endpoint));
        var version = pinnedEndpoint ? segments[toolboxIndex + 3] : null;
        if (version is not null)
        {
            ValidateSegment(version, "Toolbox version", nameof(endpoint));
        }

        var projectPath = "/" + string.Join('/', segments[..toolboxIndex]);
        var projectEndpoint = new UriBuilder(endpoint)
        {
            Path = projectPath,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;

        return new FoundryToolboxEndpointInfo(
            projectEndpoint,
            toolboxName,
            version,
            ApiVersion);
    }

    private static void ValidateProjectEndpoint(Uri projectEndpoint)
    {
        ArgumentNullException.ThrowIfNull(projectEndpoint);

        if (!projectEndpoint.IsAbsoluteUri ||
            projectEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(projectEndpoint.UserInfo) ||
            !string.IsNullOrEmpty(projectEndpoint.Query) ||
            !string.IsNullOrEmpty(projectEndpoint.Fragment))
        {
            throw new ArgumentException(
                "The project endpoint must be an absolute HTTPS URI without user information, a query, or a fragment.",
                nameof(projectEndpoint));
        }
    }

    private static void ValidateSegment(
        string value,
        string displayName,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !SafeSegmentRegex().IsMatch(value))
        {
            throw new ArgumentException(
                $"{displayName} must be one safe URI path segment containing only letters, digits, '.', '_' or '-'.",
                parameterName);
        }
    }
}

public sealed record FoundryToolboxEndpointInfo(
    Uri ProjectEndpoint,
    string ToolboxName,
    string? Version,
    string ApiVersion);
