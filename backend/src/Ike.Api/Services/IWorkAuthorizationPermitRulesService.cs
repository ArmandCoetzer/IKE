using Ike.Api.DTOs.WorkAuthorizations;

namespace Ike.Api.Services;

public interface IWorkAuthorizationPermitRulesService
{
    List<WorkAuthorizationDerivedPermitDto> GetDerivedPermits(WorkAuthorizationMasterPermitDto permit);
}
