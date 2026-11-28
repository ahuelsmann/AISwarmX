using System.Net;

namespace AISwarmX.Dashboard.Api.Security;

/// <summary>
/// Enforces the strict file denylist/whitelist so credential files and sensitive tokens are never read.
/// </summary>
public static class PathWhitelistGuard
{
    private static readonly HashSet<string> ForbiddenExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth.json",
        ".credentials.json",
        "oauth_creds.json",
        "google_accounts.json",
        "jetski-standalone-oauth-token",
        ".env"
    };

    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem",
        ".key",
        ".pfx",
        ".p12"
    };

    private static readonly string[] SensitiveNameKeywords =
    [
        "secret",
        "credential",
        "oauth",
        "token"
    ];

    /// <summary>
    /// Returns true when the given file path does not match any forbidden credential or secret pattern.
    /// </summary>
    public static bool IsAllowedFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path.Trim());
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (ForbiddenExactNames.Contains(fileName) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (ForbiddenExtensions.Contains(extension))
        {
            return false;
        }

        // Markdown documentation files in the repos (for example, docs mentioning tokens) remain readable,
        // while JSON/config/extensionless files containing credential keywords are strictly blocked.
        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var keyword in SensitiveNameKeywords)
            {
                if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Throws an <see cref="UnauthorizedAccessException"/> if the file path is forbidden.
    /// </summary>
    public static void ThrowIfForbidden(string? path)
    {
        if (!IsAllowedFilePath(path))
        {
            throw new UnauthorizedAccessException(
                $"Access to forbidden or credential-bearing file '{Path.GetFileName(path ?? string.Empty)}' was blocked.");
        }
    }
}

/// <summary>
/// Enforces that Kestrel binds exclusively to loopback addresses (127.0.0.1, localhost, or ::1).
/// </summary>
public static class LoopbackBindingGuard
{
    /// <summary>
    /// Returns true if the URL uses HTTP/HTTPS and targets a loopback host only.
    /// </summary>
    public static bool IsLoopbackUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the URL does not target loopback.
    /// </summary>
    public static string EnsureLoopbackUrl(string? url)
    {
        if (!IsLoopbackUrl(url))
        {
            throw new InvalidOperationException(
                $"Dashboard bind URL '{url}' must be a loopback address (127.0.0.1, localhost, or [::1]).");
        }

        return url!.Trim();
    }
}
