using Tradion.Api.DTOs.WorkAuthorizations;

namespace Tradion.Api.Services;

public interface IWorkAuthorizationDocumentRenderer
{
    string RenderHtml(WorkAuthorizationMasterPermitDto permit);
    byte[] RenderPdf(WorkAuthorizationMasterPermitDto permit);
}
