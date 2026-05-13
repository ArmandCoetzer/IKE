using Tradion.Api.DTOs.WorkAuthorizations;

namespace Tradion.Api.Services;

public class WorkAuthorizationPermitRulesService : IWorkAuthorizationPermitRulesService
{
    public List<WorkAuthorizationDerivedPermitDto> GetDerivedPermits(WorkAuthorizationMasterPermitDto permit)
    {
        var result = new Dictionary<string, WorkAuthorizationDerivedPermitDto>(StringComparer.OrdinalIgnoreCase);

        void Require(string key, string label, string reason)
        {
            if (result.TryGetValue(key, out var existing))
            {
                if (!existing.Reason.Contains(reason, StringComparison.OrdinalIgnoreCase))
                    existing.Reason += "; " + reason;
                return;
            }
            result[key] = new WorkAuthorizationDerivedPermitDto
            {
                PermitKey = key,
                PermitLabel = label,
                IsRequired = true,
                Reason = reason
            };
        }

        if (permit.NatureOfWorks.HotWorkOptions.Any(o => o.IsSelected))
            Require(WorkAuthorizationPermitKeys.HotWork, "Hot Work Permit", "Hot work selected.");

        if (permit.NatureOfWorks.ExcavationWork.IsSelected)
            Require(WorkAuthorizationPermitKeys.Excavation, "Excavation Permit", "Excavation work selected.");

        if (permit.NatureOfWorks.WorkAtHeightsOptions.Any(o => o.IsSelected))
            Require(WorkAuthorizationPermitKeys.WorkingAtHeights, "Working at Heights Permit", "Work at heights selected.");

        if (permit.NatureOfWorks.CleaningDegassingWork.IsSelected)
            Require(WorkAuthorizationPermitKeys.CleaningDegassing, "Cleaning-Degassing Permit", "Cleaning-degassing selected.");

        if (permit.NatureOfWorks.LiftingOptions.Any(o => o.IsSelected))
            Require(WorkAuthorizationPermitKeys.LiftingOperations, "Lifting Operations Permit", "Lifting activity selected.");

        if (permit.NatureOfWorks.ConfinedAtmosphereWork.IsSelected)
            Require(WorkAuthorizationPermitKeys.ConfinedSpaceEntry, "Confined Space Entry Permit", "Confined atmosphere work selected.");

        if (permit.NatureOfWorks.RadiographicTestingWork.IsSelected)
            Require(WorkAuthorizationPermitKeys.RadiographicTesting, "Radiographic Testing Permit", "Radiographic testing selected.");

        var isolationByElectrical = permit.NatureOfWorks.ElectricalWork.IsSelected || permit.NatureOfWorks.ElectricalWorkHv || permit.NatureOfWorks.ElectricalWorkLv;
        var isolationByControls = permit.PreventionMeasures.ElectricalControls.Any(o => o.IsSelected && o.Key.Contains("energy_isolation", StringComparison.OrdinalIgnoreCase));
        var isolationByAssociated = permit.AssociatedPermits.Permits.Any(p => p.IsRequired && p.PermitKey == WorkAuthorizationPermitKeys.EnergyIsolation);
        if (isolationByElectrical || isolationByControls || isolationByAssociated)
            Require(WorkAuthorizationPermitKeys.EnergyIsolation, "Energy Isolation Permit", "Electrical/isolation related work selected.");

        // Reinforce required permits from Associated Permits section.
        foreach (var associated in permit.AssociatedPermits.Permits.Where(p => p.IsRequired))
            Require(associated.PermitKey, associated.PermitLabel, "Marked as required in associated permits.");

        return result.Values.OrderBy(x => x.PermitLabel).ToList();
    }
}
