using Microsoft.AspNetCore.Http;

namespace Ike.Api.Helpers;

/// <summary>Magic-byte checks so extension-based allowlists cannot be bypassed with polyglot uploads.</summary>
public static class FileContentSignatureHelper
{
    private const int MaxPeek = 32;

    /// <summary>Returns null if the first bytes match the declared extension; otherwise a short error message.</summary>
    public static async Task<string?> ValidateContentMatchesExtensionAsync(IFormFile file, string extension, CancellationToken ct = default)
    {
        if (file.Length == 0)
            return "Empty file.";
        var ext = NormalizeExtension(extension);
        var peekLength = (int)Math.Min(MaxPeek, file.Length);
        var buffer = new byte[peekLength];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, peekLength), ct).ConfigureAwait(false);
            if (read < 4)
                return "File too small.";
            if (!HeaderMatchesExtension(buffer.AsSpan(0, read), ext))
                return "File content does not match the declared file type.";
        }

        return null;
    }

    public static bool HeaderMatchesExtension(ReadOnlySpan<byte> header, string extensionLowerWithDot)
    {
        var ext = NormalizeExtension(extensionLowerWithDot);
        return ext switch
        {
            ".pdf" => IsPdf(header),
            ".png" => IsPng(header),
            ".jpg" or ".jpeg" => IsJpeg(header),
            ".gif" => IsGif(header),
            ".webp" => IsWebpImage(header),
            ".mp4" => IsMp4(header),
            ".webm" => IsEbmlWebm(header),
            _ => false
        };
    }

    private static string NormalizeExtension(string extension)
    {
        var e = extension.Trim().ToLowerInvariant();
        return e.StartsWith('.') ? e : "." + e;
    }

    private static bool IsPdf(ReadOnlySpan<byte> h) =>
        h.Length >= 5
        && h[0] == (byte)'%'
        && h[1] == (byte)'P'
        && h[2] == (byte)'D'
        && h[3] == (byte)'F'
        && h[4] == (byte)'-';

    private static bool IsPng(ReadOnlySpan<byte> h) =>
        h.Length >= 8
        && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
        && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A;

    private static bool IsJpeg(ReadOnlySpan<byte> h) =>
        h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;

    private static bool IsGif(ReadOnlySpan<byte> h) =>
        h.Length >= 6
        && h[0] == (byte)'G' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'8'
        && (h[4] == (byte)'7' || h[4] == (byte)'9')
        && h[5] == (byte)'a';

    private static bool IsWebpImage(ReadOnlySpan<byte> h) =>
        h.Length >= 12
        && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
        && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P';

    /// <summary>ISO BMFF: size (4) + "ftyp" (4) at offset 4.</summary>
    private static bool IsMp4(ReadOnlySpan<byte> h) =>
        h.Length >= 12
        && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p';

    /// <summary>Matroska / WebM EBML header.</summary>
    private static bool IsEbmlWebm(ReadOnlySpan<byte> h) =>
        h.Length >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3;
}
