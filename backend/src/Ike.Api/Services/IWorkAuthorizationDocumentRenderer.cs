using Ike.Api.DTOs.WorkAuthorizations;

namespace Ike.Api.Services;

public interface IWorkAuthorizationDocumentRenderer
{
    string RenderHtml(WorkAuthorizationMasterPermitDto permit);
    byte[] RenderPdf(WorkAuthorizationMasterPermitDto permit);
}
