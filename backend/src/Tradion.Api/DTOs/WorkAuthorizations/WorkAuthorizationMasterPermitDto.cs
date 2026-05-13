namespace Tradion.Api.DTOs.WorkAuthorizations;

public static class WorkAuthorizationPermitKeys
{
    public const string Excavation = "excavation";
    public const string WorkingAtHeights = "working_at_heights";
    public const string CleaningDegassing = "cleaning_degassing";
    public const string HotWork = "hot_work";
    public const string RadiographicTesting = "radiographic_testing";
    public const string EnergyIsolation = "energy_isolation";
    public const string LiftingOperations = "lifting_operations";
    public const string ConfinedSpaceEntry = "confined_space_entry";
}

public class WorkAuthorizationMasterPermitDto
{
    public Guid PermitGuid { get; set; }
    public string WorkAuthorizationNumber { get; set; } = string.Empty;

    public WorkAuthorizationHeaderDto Header { get; set; } = new();
    public WorkAuthorizationLocationTaskDto LocationTask { get; set; } = new();
    public WorkAuthorizationAssociatedPermitsSectionDto AssociatedPermits { get; set; } = new();
    public WorkAuthorizationInterferenceSectionDto Interference { get; set; } = new();
    public WorkAuthorizationCompulsorySafetyMeasuresDto CompulsorySafetyMeasures { get; set; } = new();
    public WorkAuthorizationNatureOfWorksSectionDto NatureOfWorks { get; set; } = new();
    public WorkAuthorizationNatureOfRisksSectionDto NatureOfRisks { get; set; } = new();
    public WorkAuthorizationPreventionMeasuresSectionDto PreventionMeasures { get; set; } = new();
    public WorkAuthorizationDeclarationSectionDto Declaration { get; set; } = new();

    public List<WorkAuthorizationRevalidationEntryDto> Revalidations { get; set; } = new();
    public List<WorkAuthorizationHandbackEntryDto> HandBackEntries { get; set; } = new();
    public WorkAuthorizationWithdrawalSectionDto Withdrawal { get; set; } = new();
    public List<WorkAuthorizationDerivedPermitDto> DerivedRequiredPermits { get; set; } = new();

    public string? Notes { get; set; }
    public DateTime CreatedDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDateUtc { get; set; }
}

public class WorkAuthorizationHeaderDto
{
    public DateTime? IssueDate { get; set; }
    public DateTime? ValidFromDate { get; set; }
    public DateTime? ValidToDate { get; set; }
    public TimeSpan? ValidFromTime { get; set; }
    public TimeSpan? ValidToTime { get; set; }
    public int? NumberOfWorkers { get; set; }
    public string? SiteName { get; set; }
    public string? ScopeOfWorks { get; set; }
    public string? AtexZone { get; set; }
}

public class WorkAuthorizationLocationTaskDto
{
    public string? ContractorCompany { get; set; }
    public string? LocationOfOperation { get; set; }
    public string? TaskDescription { get; set; }
}

public class WorkAuthorizationAssociatedPermitsSectionDto
{
    public bool IsProject { get; set; }
    public bool IsMaintenance { get; set; }
    public string? PreventionPlanReferenceNumber { get; set; }
    public bool SafetyConditionCompleted { get; set; }
    public List<WorkAuthorizationAssociatedPermitDto> Permits { get; set; } = new();
}

public class WorkAuthorizationAssociatedPermitDto
{
    public string PermitKey { get; set; } = string.Empty;
    public string PermitLabel { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class WorkAuthorizationInterferenceSectionDto
{
    public string? FuelDeliveryReceiptScheduledAt { get; set; }
    public bool HasOtherWorkPlannedForDay { get; set; }
    public string? OtherWorkPlannedForDayDetails { get; set; }
    public string? OtherWorkPlannedForDayReferenceNumber { get; set; }
    public bool HasPresenceOfGasCylindersOrBarrels { get; set; }
    public string? PresenceOfGasCylindersOrBarrelsDetails { get; set; }
    public bool HasOtherNearbyWorkPlanned { get; set; }
    public string? OtherNearbyWorkPlannedDetails { get; set; }
    public string? OtherNearbyWorkReferenceNumber { get; set; }
}

public class WorkAuthorizationCompulsorySafetyMeasuresDto
{
    public bool PersonnelInformed { get; set; }
    public bool MobilePhonesCamerasEtcSwitchedOff { get; set; }
    public bool ClosureOfStation { get; set; }
    public bool HotWorkRestrictedInClassifiedAreas { get; set; }
    public bool DistributionStoppagePartial { get; set; }
    public bool DistributionStoppageTotal { get; set; }
    public bool ProtectiveClothingRequired { get; set; }
    public bool HearingProtectionRequired { get; set; }
    public bool HardHatRequired { get; set; }
    public bool GogglesOrFaceShieldRequired { get; set; }
    public bool VisibilityVestRequired { get; set; }
    public bool SteelToeCapShoesRequired { get; set; }
    public bool HighVisibilityCoverallRequired { get; set; }
    public bool DustMaskRequired { get; set; }
    public bool GlovesRequired { get; set; }
    public string? AdditionalNotes { get; set; }
}

public class WorkAuthorizationNatureOfWorksSectionDto
{
    public List<ToggleOptionDto> MovementCirculationOptions { get; set; } = new();
    public ToggleOptionWithCommentDto WorkInExplosionRiskZone { get; set; } = new();
    public List<ToggleOptionDto> HotWorkOptions { get; set; } = new();
    public ToggleOptionWithCommentDto EquipmentTools { get; set; } = new();
    public ToggleOptionWithCommentDto DangerousMachineryWork { get; set; } = new();
    public ToggleOptionWithCommentDto HazardousChemicalProducts { get; set; } = new();
    public ToggleOptionWithCommentDto ExtremeTemperatureWork { get; set; } = new();
    public ToggleOptionWithCommentDto PressurisedEquipmentUse { get; set; } = new();
    public ToggleOptionWithCommentDto ManualHandling { get; set; } = new();
    public string? ManualHandlingTypesAndWeight { get; set; }
    public ToggleOptionWithCommentDto ExcavationWork { get; set; } = new();
    public ToggleOptionWithCommentDto ElectricalWork { get; set; } = new();
    public bool ElectricalWorkLv { get; set; }
    public bool ElectricalWorkHv { get; set; }
    public ToggleOptionWithCommentDto ConfinedAtmosphereWork { get; set; } = new();
    public List<ToggleOptionDto> LiftingOptions { get; set; } = new();
    public List<ToggleOptionDto> WorkAtHeightsOptions { get; set; } = new();
    public ToggleOptionWithCommentDto DrainingRinsingWork { get; set; } = new();
    public ToggleOptionWithCommentDto CleaningDegassingWork { get; set; } = new();
    public ToggleOptionWithCommentDto RadiographicTestingWork { get; set; } = new();
    public ToggleOptionWithCommentDto NoisyWork { get; set; } = new();
    public ToggleOptionWithCommentDto OtherWork { get; set; } = new();
}

public class WorkAuthorizationNatureOfRisksSectionDto
{
    public List<ToggleOptionDto> TrafficAndMovementRisks { get; set; } = new();
    public List<ToggleOptionDto> FireExplosionRisks { get; set; } = new();
    public List<ToggleOptionDto> MechanicalRisks { get; set; } = new();
    public List<ToggleOptionDto> ChemicalThermalRisks { get; set; } = new();
    public List<ToggleOptionDto> ManualHandlingRisks { get; set; } = new();
    public List<ToggleOptionDto> ExcavationRisks { get; set; } = new();
    public List<ToggleOptionDto> ElectricalRisks { get; set; } = new();
    public List<ToggleOptionDto> ConfinedSpaceRisks { get; set; } = new();
    public List<ToggleOptionDto> OverheadAndDroppedObjectRisks { get; set; } = new();
    public List<ToggleOptionDto> HeightRisks { get; set; } = new();
    public List<ToggleOptionDto> RadiographicRisks { get; set; } = new();
    public List<ToggleOptionDto> NoiseRisks { get; set; } = new();
    public string? OtherRisksNotes { get; set; }
}

public class WorkAuthorizationPreventionMeasuresSectionDto
{
    public bool ActivitiesHaltedCompletely { get; set; }
    public bool ActivitiesHaltedPartially { get; set; }
    public string? ActivitiesHaltedSpecify { get; set; }
    public List<ToggleOptionDto> TrafficAndOperationalControls { get; set; } = new();
    public List<ToggleOptionDto> ExplosionAndAtexControls { get; set; } = new();
    public List<ToggleOptionDto> MechanicalControls { get; set; } = new();
    public List<ToggleOptionDto> ChemicalAndPpeControls { get; set; } = new();
    public List<ToggleOptionDto> PressureAndManualHandlingControls { get; set; } = new();
    public List<ToggleOptionDto> ExcavationControls { get; set; } = new();
    public List<ToggleOptionDto> ElectricalControls { get; set; } = new();
    public List<ToggleOptionDto> ConfinedSpaceControls { get; set; } = new();
    public List<ToggleOptionDto> LiftingControls { get; set; } = new();
    public List<ToggleOptionDto> WorkingAtHeightsControls { get; set; } = new();
    public List<ToggleOptionDto> CleaningDegassingControls { get; set; } = new();
    public List<ToggleOptionDto> RadiographicControls { get; set; } = new();
    public List<ToggleOptionDto> NoiseControls { get; set; } = new();
    public string? OtherPreventionMeasuresNotes { get; set; }
}

public class WorkAuthorizationDeclarationSectionDto
{
    public WorkAuthorizationSignatureDto IssuingAuthority { get; set; } = new();
    public WorkAuthorizationSignatureDto PerformingAuthority { get; set; } = new();
    public WorkAuthorizationSignatureDto SiteAcknowledgement { get; set; } = new();
    public string? DeclarationText { get; set; }
}

public class WorkAuthorizationSignatureDto
{
    public string? Name { get; set; }
    public string? Role { get; set; }
    public DateTime? SignedDateTime { get; set; }
    public string? SignatureImageUrl { get; set; }
    public string? SignatureImageBase64 { get; set; }
}

public class WorkAuthorizationRevalidationEntryDto
{
    public DateTime? Date { get; set; }
    public TimeSpan? TimeFrom { get; set; }
    public TimeSpan? TimeTo { get; set; }
    public string? IssuingAuthorityName { get; set; }
    public string? IssuingAuthoritySignatureImageUrl { get; set; }
    public string? PerformingAuthorityName { get; set; }
    public string? PerformingAuthoritySignatureImageUrl { get; set; }
}

public class WorkAuthorizationHandbackEntryDto
{
    public DateTime? Date { get; set; }
    public bool? WorksCompleted { get; set; }
    public string? IssuingAuthorityName { get; set; }
    public string? IssuingAuthoritySignatureImageUrl { get; set; }
    public string? PerformingAuthorityName { get; set; }
    public string? PerformingAuthoritySignatureImageUrl { get; set; }
}

public class WorkAuthorizationWithdrawalSectionDto
{
    public bool IsWithdrawn { get; set; }
    public bool ScopeOfWorkChanges { get; set; }
    public bool PermitRulesViolation { get; set; }
    public bool AccidentOccurrence { get; set; }
    public bool IssuingOrPerformingAuthorityNotOnSite { get; set; }
    public string? OtherReason { get; set; }
    public string? Notes { get; set; }
}

public class WorkAuthorizationDerivedPermitDto
{
    public string PermitKey { get; set; } = string.Empty;
    public string PermitLabel { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ToggleOptionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public class ToggleOptionWithCommentDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? Comment { get; set; }
}

public class ToggleOptionWithReferenceDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Comment { get; set; }
}

public class PermitOptionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class WorkAuthorizationDocumentViewModel
{
    public WorkAuthorizationMasterPermitDto Permit { get; set; } = new();
    public List<WorkAuthorizationDerivedPermitDto> DerivedPermits { get; set; } = new();
    public DateTime RenderedAtUtc { get; set; } = DateTime.UtcNow;
}
