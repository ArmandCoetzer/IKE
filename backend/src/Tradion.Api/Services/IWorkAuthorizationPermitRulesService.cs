using Tradion.Api.DTOs.WorkAuthorizations;

namespace Tradion.Api.Services;

public interface IWorkAuthorizationPermitRulesService
{
    List<WorkAuthorizationDerivedPermitDto> GetDerivedPermits(WorkAuthorizationMasterPermitDto permit);
}
