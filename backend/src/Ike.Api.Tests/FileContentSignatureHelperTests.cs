using System.Text;
using Microsoft.AspNetCore.Http;
using Ike.Api.Helpers;
using Xunit;

namespace Ike.Api.Tests;

public class FileContentSignatureHelperTests
{
    [Theory]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 })]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 })]
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData(".jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0x00 })]
    [InlineData(".gif", new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' })]
    [InlineData(".webp", new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' })]
    [InlineData(".mp4", new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' })]
    [InlineData(".webm", new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01 })]
    public void HeaderMatchesExtension_AcceptsKnownSignatures(string ext, byte[] header)
    {
        Assert.True(FileContentSignatureHelper.HeaderMatchesExtension(header, ext));
    }

    [Fact]
    public void HeaderMatchesExtension_RejectsPdfWithJpegMagic()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.False(FileContentSignatureHelper.HeaderMatchesExtension(jpeg, ".pdf"));
    }

    [Fact]
    public async Task ValidateContentMatchesExtensionAsync_FormFile_RejectsMismatch()
    {
        var bytes = Encoding.UTF8.GetBytes("not a real pdf");
        await using var ms = new MemoryStream(bytes);
        var form = new FormFile(ms, 0, bytes.Length, "file", "x.pdf");
        var err = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(form, ".pdf");
        Assert.NotNull(err);
    }
}
