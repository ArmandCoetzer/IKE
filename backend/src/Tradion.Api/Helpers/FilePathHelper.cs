namespace Tradion.Api.Helpers;

/// <summary>
/// Validates and normalizes file paths for secure access under the uploads directory.
/// Prevents path traversal (..) and ensures paths are under uploads/.
/// </summary>
public static class FilePathHelper
{
    private static readonly char[] PathSeparators = { '/', '\\' };

    /// <summary>
    /// Validates that the path is safe: under uploads/, no .., normalized.
    /// Returns the validated normalized path, or null if invalid.
    /// </summary>
    /// <param name="storedPath">Relative path from DB (e.g. uploads/job-documents/abc.pdf)</param>
    /// <returns>Normalized path if valid; null if invalid.</returns>
    public static string? ValidateAndNormalize(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        var normalized = storedPath
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        if (string.IsNullOrEmpty(normalized))
            return null;

        // Must start with uploads/
        if (!normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            return null;

        // No path traversal
        if (normalized.Contains("..", StringComparison.Ordinal))
            return null;

        return normalized;
    }
}
