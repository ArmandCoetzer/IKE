namespace Tradion.Api.Services;

/// <summary>Single professional PDF for non–Work Authorisation permits (form, checklist, embedded client signature).</summary>
public interface IChildPermitDocumentationPdfRenderer
{
    Task<byte[]?> RenderAsync(Guid jobPermitId, CancellationToken ct = default);
}
