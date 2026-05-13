namespace Tradion.Api.DTOs.WorkAuthorizations;

/// <summary>
/// Canonical wording aligned to the TotalEnergies Work Authorisation (master permit) source form.
/// Used to seed new permits and to refresh labels when loading older snapshots.
/// </summary>
public static class WorkAuthorizationMasterPermitDefaults
{
    public const string DeclarationTextFull =
        "I HAVE PERSONALLY DISCUSSED EVERY SECTION WITH THE PERFORMING AUTHORITY AND EXPLAINED THE REQUIREMENTS OF THIS WORK AUTHORISATION, ASSOCIATED PERMITS AND DOCUMENTS REQUIRED. " +
        "We (Issuing and Performing Authority) have personally examined the area where the works is to be carried out and hereby confirm that we are satisfied with the contents of this permit, associated certificates and documents required; all necessary persons included have been advised on the works to be conducted. " +
        "We are aware of the safety rules, emergency and evacuation procedures of the site. Conditions are safe for the works to commence.";

    public const string HeaderStipulationShort =
        "THE AUTHORISATION IS ISSUED FOR A MAXIMUM DURATION OF 5 CONSECUTIVE DAYS UNDER STRICT STIPULATION THAT: THE WORK CONDITIONS ARE NOT MODIFIED. IT MUST BE RE-VALIDATED EACH DAY BEFORE STARTING WORK.";

    public static WorkAuthorizationMasterPermitDto CreateTemplate(Guid permitGuid, string workAuthorizationNumber)
    {
        var dto = new WorkAuthorizationMasterPermitDto
        {
            PermitGuid = permitGuid,
            WorkAuthorizationNumber = workAuthorizationNumber,
            CreatedDateUtc = DateTime.UtcNow,
            Declaration = new WorkAuthorizationDeclarationSectionDto
            {
                DeclarationText = DeclarationTextFull,
                IssuingAuthority = new WorkAuthorizationSignatureDto { Role = "ISSUING AUTHORITY" },
                PerformingAuthority = new WorkAuthorizationSignatureDto { Role = "PERFORMING AUTHORITY" },
                SiteAcknowledgement = new WorkAuthorizationSignatureDto { Role = "SITE ACKNOWLEDGEMENT" }
            },
            AssociatedPermits = new WorkAuthorizationAssociatedPermitsSectionDto
            {
                Permits =
                {
                    new() { PermitKey = WorkAuthorizationPermitKeys.Excavation, PermitLabel = "EXCAVATION PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.WorkingAtHeights, PermitLabel = "WORKING AT HEIGHTS PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.CleaningDegassing, PermitLabel = "CLEANING-DEGASSING PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.HotWork, PermitLabel = "HOT WORK PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.RadiographicTesting, PermitLabel = "RADIOGRAPHIC TESTING PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.EnergyIsolation, PermitLabel = "ENERGY ISOLATION PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.LiftingOperations, PermitLabel = "LIFTING OPERATIONS PERMIT" },
                    new() { PermitKey = WorkAuthorizationPermitKeys.ConfinedSpaceEntry, PermitLabel = "CONFINED SPACE ENTRY PERMIT" }
                }
            },
            Interference = new WorkAuthorizationInterferenceSectionDto(),
            CompulsorySafetyMeasures = new WorkAuthorizationCompulsorySafetyMeasuresDto(),
            NatureOfWorks = CreateNatureOfWorks(),
            NatureOfRisks = CreateNatureOfRisks(),
            PreventionMeasures = CreatePreventionMeasures(),
            Withdrawal = new WorkAuthorizationWithdrawalSectionDto()
        };
        return dto;
    }

    /// <summary>
    /// Refreshes labels and inserts missing options from the template while preserving selections and comments.
    /// </summary>
    public static void MergeInto(WorkAuthorizationMasterPermitDto existing, WorkAuthorizationMasterPermitDto template)
    {
        existing.NatureOfWorks ??= new WorkAuthorizationNatureOfWorksSectionDto();
        existing.NatureOfRisks ??= new WorkAuthorizationNatureOfRisksSectionDto();
        existing.PreventionMeasures ??= new WorkAuthorizationPreventionMeasuresSectionDto();
        existing.AssociatedPermits ??= new WorkAuthorizationAssociatedPermitsSectionDto();
        existing.AssociatedPermits.Permits ??= new List<WorkAuthorizationAssociatedPermitDto>();

        EnsureToggleLists(existing.NatureOfWorks, existing.NatureOfRisks, existing.PreventionMeasures);

        MergeToggleLists(existing.NatureOfWorks.MovementCirculationOptions, template.NatureOfWorks.MovementCirculationOptions);
        MergeToggleLists(existing.NatureOfWorks.HotWorkOptions, template.NatureOfWorks.HotWorkOptions);
        MergeToggleLists(existing.NatureOfWorks.LiftingOptions, template.NatureOfWorks.LiftingOptions);
        MergeToggleLists(existing.NatureOfWorks.WorkAtHeightsOptions, template.NatureOfWorks.WorkAtHeightsOptions);

        existing.NatureOfWorks.WorkInExplosionRiskZone = MergeOrCreateToggle(existing.NatureOfWorks.WorkInExplosionRiskZone, template.NatureOfWorks.WorkInExplosionRiskZone);
        existing.NatureOfWorks.EquipmentTools = MergeOrCreateToggle(existing.NatureOfWorks.EquipmentTools, template.NatureOfWorks.EquipmentTools);
        existing.NatureOfWorks.DangerousMachineryWork = MergeOrCreateToggle(existing.NatureOfWorks.DangerousMachineryWork, template.NatureOfWorks.DangerousMachineryWork);
        existing.NatureOfWorks.HazardousChemicalProducts = MergeOrCreateToggle(existing.NatureOfWorks.HazardousChemicalProducts, template.NatureOfWorks.HazardousChemicalProducts);
        existing.NatureOfWorks.ExtremeTemperatureWork = MergeOrCreateToggle(existing.NatureOfWorks.ExtremeTemperatureWork, template.NatureOfWorks.ExtremeTemperatureWork);
        existing.NatureOfWorks.PressurisedEquipmentUse = MergeOrCreateToggle(existing.NatureOfWorks.PressurisedEquipmentUse, template.NatureOfWorks.PressurisedEquipmentUse);
        existing.NatureOfWorks.ManualHandling = MergeOrCreateToggle(existing.NatureOfWorks.ManualHandling, template.NatureOfWorks.ManualHandling);
        existing.NatureOfWorks.ExcavationWork = MergeOrCreateToggle(existing.NatureOfWorks.ExcavationWork, template.NatureOfWorks.ExcavationWork);
        existing.NatureOfWorks.ElectricalWork = MergeOrCreateToggle(existing.NatureOfWorks.ElectricalWork, template.NatureOfWorks.ElectricalWork);
        existing.NatureOfWorks.ConfinedAtmosphereWork = MergeOrCreateToggle(existing.NatureOfWorks.ConfinedAtmosphereWork, template.NatureOfWorks.ConfinedAtmosphereWork);
        existing.NatureOfWorks.DrainingRinsingWork = MergeOrCreateToggle(existing.NatureOfWorks.DrainingRinsingWork, template.NatureOfWorks.DrainingRinsingWork);
        existing.NatureOfWorks.CleaningDegassingWork = MergeOrCreateToggle(existing.NatureOfWorks.CleaningDegassingWork, template.NatureOfWorks.CleaningDegassingWork);
        existing.NatureOfWorks.RadiographicTestingWork = MergeOrCreateToggle(existing.NatureOfWorks.RadiographicTestingWork, template.NatureOfWorks.RadiographicTestingWork);
        existing.NatureOfWorks.NoisyWork = MergeOrCreateToggle(existing.NatureOfWorks.NoisyWork, template.NatureOfWorks.NoisyWork);
        existing.NatureOfWorks.OtherWork = MergeOrCreateToggle(existing.NatureOfWorks.OtherWork, template.NatureOfWorks.OtherWork);

        MergeToggleLists(existing.NatureOfRisks.TrafficAndMovementRisks, template.NatureOfRisks.TrafficAndMovementRisks);
        MergeToggleLists(existing.NatureOfRisks.FireExplosionRisks, template.NatureOfRisks.FireExplosionRisks);
        MergeToggleLists(existing.NatureOfRisks.MechanicalRisks, template.NatureOfRisks.MechanicalRisks);
        MergeToggleLists(existing.NatureOfRisks.ChemicalThermalRisks, template.NatureOfRisks.ChemicalThermalRisks);
        MergeToggleLists(existing.NatureOfRisks.ManualHandlingRisks, template.NatureOfRisks.ManualHandlingRisks);
        MergeToggleLists(existing.NatureOfRisks.ExcavationRisks, template.NatureOfRisks.ExcavationRisks);
        MergeToggleLists(existing.NatureOfRisks.ElectricalRisks, template.NatureOfRisks.ElectricalRisks);
        MergeToggleLists(existing.NatureOfRisks.ConfinedSpaceRisks, template.NatureOfRisks.ConfinedSpaceRisks);
        MergeToggleLists(existing.NatureOfRisks.OverheadAndDroppedObjectRisks, template.NatureOfRisks.OverheadAndDroppedObjectRisks);
        MergeToggleLists(existing.NatureOfRisks.HeightRisks, template.NatureOfRisks.HeightRisks);
        MergeToggleLists(existing.NatureOfRisks.RadiographicRisks, template.NatureOfRisks.RadiographicRisks);
        MergeToggleLists(existing.NatureOfRisks.NoiseRisks, template.NatureOfRisks.NoiseRisks);

        MergeToggleLists(existing.PreventionMeasures.TrafficAndOperationalControls, template.PreventionMeasures.TrafficAndOperationalControls);
        MergeToggleLists(existing.PreventionMeasures.ExplosionAndAtexControls, template.PreventionMeasures.ExplosionAndAtexControls);
        MergeToggleLists(existing.PreventionMeasures.MechanicalControls, template.PreventionMeasures.MechanicalControls);
        MergeToggleLists(existing.PreventionMeasures.ChemicalAndPpeControls, template.PreventionMeasures.ChemicalAndPpeControls);
        MergeToggleLists(existing.PreventionMeasures.PressureAndManualHandlingControls, template.PreventionMeasures.PressureAndManualHandlingControls);
        MergeToggleLists(existing.PreventionMeasures.ExcavationControls, template.PreventionMeasures.ExcavationControls);
        MergeToggleLists(existing.PreventionMeasures.ElectricalControls, template.PreventionMeasures.ElectricalControls);
        MergeToggleLists(existing.PreventionMeasures.ConfinedSpaceControls, template.PreventionMeasures.ConfinedSpaceControls);
        MergeToggleLists(existing.PreventionMeasures.LiftingControls, template.PreventionMeasures.LiftingControls);
        MergeToggleLists(existing.PreventionMeasures.WorkingAtHeightsControls, template.PreventionMeasures.WorkingAtHeightsControls);
        MergeToggleLists(existing.PreventionMeasures.CleaningDegassingControls, template.PreventionMeasures.CleaningDegassingControls);
        MergeToggleLists(existing.PreventionMeasures.RadiographicControls, template.PreventionMeasures.RadiographicControls);
        MergeToggleLists(existing.PreventionMeasures.NoiseControls, template.PreventionMeasures.NoiseControls);

        MergeAssociatedPermits(existing.AssociatedPermits.Permits, template.AssociatedPermits.Permits);
    }

    private static void MergeAssociatedPermits(List<WorkAuthorizationAssociatedPermitDto> existing, List<WorkAuthorizationAssociatedPermitDto> template)
    {
        if (template.Count == 0) return;
        var byKey = existing.ToDictionary(p => p.PermitKey, StringComparer.OrdinalIgnoreCase);
        foreach (var t in template)
        {
            if (byKey.TryGetValue(t.PermitKey, out var e))
                e.PermitLabel = t.PermitLabel;
            else
                existing.Add(new WorkAuthorizationAssociatedPermitDto
                {
                    PermitKey = t.PermitKey,
                    PermitLabel = t.PermitLabel,
                    IsRequired = false
                });
        }
    }

    private static void MergeToggleLists(List<ToggleOptionDto> existing, List<ToggleOptionDto> template)
    {
        if (template.Count == 0) return;
        var selected = existing.ToDictionary(x => x.Key, x => x.IsSelected, StringComparer.OrdinalIgnoreCase);
        existing.Clear();
        foreach (var t in template)
        {
            existing.Add(new ToggleOptionDto
            {
                Key = t.Key,
                Label = t.Label,
                IsSelected = selected.TryGetValue(t.Key, out var s) && s
            });
        }
    }

    private static void EnsureToggleLists(
        WorkAuthorizationNatureOfWorksSectionDto works,
        WorkAuthorizationNatureOfRisksSectionDto risks,
        WorkAuthorizationPreventionMeasuresSectionDto prevention)
    {
        works.MovementCirculationOptions ??= new List<ToggleOptionDto>();
        works.HotWorkOptions ??= new List<ToggleOptionDto>();
        works.LiftingOptions ??= new List<ToggleOptionDto>();
        works.WorkAtHeightsOptions ??= new List<ToggleOptionDto>();

        risks.TrafficAndMovementRisks ??= new List<ToggleOptionDto>();
        risks.FireExplosionRisks ??= new List<ToggleOptionDto>();
        risks.MechanicalRisks ??= new List<ToggleOptionDto>();
        risks.ChemicalThermalRisks ??= new List<ToggleOptionDto>();
        risks.ManualHandlingRisks ??= new List<ToggleOptionDto>();
        risks.ExcavationRisks ??= new List<ToggleOptionDto>();
        risks.ElectricalRisks ??= new List<ToggleOptionDto>();
        risks.ConfinedSpaceRisks ??= new List<ToggleOptionDto>();
        risks.OverheadAndDroppedObjectRisks ??= new List<ToggleOptionDto>();
        risks.HeightRisks ??= new List<ToggleOptionDto>();
        risks.RadiographicRisks ??= new List<ToggleOptionDto>();
        risks.NoiseRisks ??= new List<ToggleOptionDto>();

        prevention.TrafficAndOperationalControls ??= new List<ToggleOptionDto>();
        prevention.ExplosionAndAtexControls ??= new List<ToggleOptionDto>();
        prevention.MechanicalControls ??= new List<ToggleOptionDto>();
        prevention.ChemicalAndPpeControls ??= new List<ToggleOptionDto>();
        prevention.PressureAndManualHandlingControls ??= new List<ToggleOptionDto>();
        prevention.ExcavationControls ??= new List<ToggleOptionDto>();
        prevention.ElectricalControls ??= new List<ToggleOptionDto>();
        prevention.ConfinedSpaceControls ??= new List<ToggleOptionDto>();
        prevention.LiftingControls ??= new List<ToggleOptionDto>();
        prevention.WorkingAtHeightsControls ??= new List<ToggleOptionDto>();
        prevention.CleaningDegassingControls ??= new List<ToggleOptionDto>();
        prevention.RadiographicControls ??= new List<ToggleOptionDto>();
        prevention.NoiseControls ??= new List<ToggleOptionDto>();
    }

    private static ToggleOptionWithCommentDto MergeOrCreateToggle(ToggleOptionWithCommentDto? existing, ToggleOptionWithCommentDto template)
    {
        if (existing == null)
        {
            return new ToggleOptionWithCommentDto
            {
                Key = template.Key,
                Label = template.Label,
                IsSelected = false,
                Comment = string.Empty
            };
        }

        MergeToggleComment(existing, template);
        return existing;
    }

    private static void MergeToggleComment(ToggleOptionWithCommentDto target, ToggleOptionWithCommentDto template)
    {
        var sel = target.IsSelected;
        var comment = target.Comment;
        target.Key = template.Key;
        target.Label = template.Label;
        target.IsSelected = sel;
        target.Comment = comment;
    }

    private static WorkAuthorizationNatureOfWorksSectionDto CreateNatureOfWorks() => new()
    {
        MovementCirculationOptions =
        {
            new() { Key = "movement_vehicles_machinery_pedestrians", Label = "MOVEMENT OF VEHICLES / MACHINERY / PEDESTRIANS" },
            new() { Key = "movement_level_surface", Label = "MOVEMENT ON LEVEL SURFACE" },
            new() { Key = "work_under_on_beside_water", Label = "WORK UNDER / ON / BESIDE WATER" }
        },
        WorkInExplosionRiskZone = new()
        {
            Key = "explosion_risk_zone",
            Label = "WORK IN ZONE AT RISK OF EXPLOSION"
        },
        HotWorkOptions =
        {
            new() { Key = "grinding", Label = "GRINDING" },
            new() { Key = "drilling", Label = "DRILLING" },
            new() { Key = "welding", Label = "WELDING" },
            new() { Key = "brazing", Label = "BRAZING" },
            new() { Key = "cutting", Label = "CUTTING (arc, oxygen, saw)" }
        },
        EquipmentTools = new() { Key = "equipment_tools", Label = "EQUIPMENT / TOOLS" },
        DangerousMachineryWork = new() { Key = "dangerous_machinery", Label = "WORK ON DANGEROUS MACHINERY" },
        HazardousChemicalProducts = new() { Key = "hazardous_chemical", Label = "HAZARDOUS CHEMICAL PRODUCTS" },
        ExtremeTemperatureWork = new()
        {
            Key = "extreme_temperature",
            Label = "WORK ON INSTALLATIONS AT VERY LOW < -10°C / VERY HIGH TEMPERATURE > 40°C"
        },
        PressurisedEquipmentUse = new()
        {
            Key = "pressurised",
            Label = "USE OF PRESSURISED EQUIPMENT (e.g. high pressure cleaning / sandblasting)"
        },
        ManualHandling = new() { Key = "manual_handling", Label = "MANUAL HANDLING" },
        ExcavationWork = new() { Key = "excavation", Label = "EXCAVATION WORK" },
        ElectricalWork = new() { Key = "electrical", Label = "ELECTRICAL WORK" },
        ConfinedAtmosphereWork = new() { Key = "confined_atmosphere", Label = "WORK IN CONFINED ATMOSPHERE" },
        LiftingOptions =
        {
            new() { Key = "lifting_beam_elevator", Label = "LIFTING BEAM / ELEVATOR" },
            new() { Key = "crane_truck", Label = "CRANE / CRANE TRUCK" },
            new() { Key = "bridge_crane", Label = "BRIDGE CRANE" },
            new() { Key = "warehousing_carrier", Label = "WAREHOUSING CARRIER" },
            new() { Key = "winches", Label = "WINCHES" }
        },
        WorkAtHeightsOptions =
        {
            new() { Key = "ladder_stepladder", Label = "LADDER / STEPLADDER" },
            new() { Key = "scaffolding", Label = "SCAFFOLDING" },
            new() { Key = "rope_access", Label = "ROPE ACCESS" },
            new() { Key = "mewp", Label = "MEWP" },
            new() { Key = "canopies_roofs", Label = "WORK ON CANOPIES / ROOFS" }
        },
        DrainingRinsingWork = new() { Key = "draining_rinsing", Label = "DRAINING / RINSING WORK" },
        CleaningDegassingWork = new() { Key = "cleaning_degassing", Label = "CLEANING-DEGASSING WORK" },
        RadiographicTestingWork = new()
        {
            Key = "radiographic",
            Label = "RADIOGRAPHIC TESTING (IONISING RADIATION)"
        },
        NoisyWork = new() { Key = "noisy", Label = "NOISY WORK" },
        OtherWork = new() { Key = "other", Label = "OTHER" }
    };

    private static WorkAuthorizationNatureOfRisksSectionDto CreateNatureOfRisks() => new()
    {
        TrafficAndMovementRisks =
        {
            new() { Key = "collision_pedestrians", Label = "COLLISION WITH PEDESTRIANS" },
            new() { Key = "collision_vehicles", Label = "COLLISION WITH PRIVATE & OTHER VEHICLES" },
            new() { Key = "collision_crash_crushing", Label = "COLLISION / CRASH / CRUSHING" },
            new() { Key = "slip_trips", Label = "SLIP AND TRIPS" },
            new() { Key = "drowning", Label = "DROWNING" }
        },
        FireExplosionRisks =
        {
            new() { Key = "start_fire_explosion", Label = "START OF FIRE / EXPLOSION" },
            new() { Key = "flammable_materials", Label = "PRESENCE OF FLAMMABLE MATERIALS" },
            new() { Key = "fire_explosion", Label = "FIRE / EXPLOSION" }
        },
        MechanicalRisks =
        {
            new() { Key = "splashes", Label = "SPLASHES" },
            new() { Key = "explosion_mech", Label = "EXPLOSION" },
            new() { Key = "cutting", Label = "CUTTING" },
            new() { Key = "pinching", Label = "PINCHING" },
            new() { Key = "trapping", Label = "TRAPPING" },
            new() { Key = "moving_parts", Label = "MOVING PART(S)" },
            new() { Key = "entanglement", Label = "ENTANGLEMENT" },
            new() { Key = "vibrations", Label = "VIBRATIONS" },
            new() { Key = "whipping", Label = "WHIPPING" },
            new() { Key = "accidental_startup", Label = "ACCIDENTAL STARTUP" },
            new() { Key = "crushing", Label = "CRUSHING" }
        },
        ChemicalThermalRisks =
        {
            new() { Key = "chemical_burns", Label = "CHEMICAL BURNS" },
            new() { Key = "ingestion_intoxication", Label = "INGESTION / INTOXICATION" },
            new() { Key = "inhalation", Label = "INHALATION" },
            new() { Key = "absorption", Label = "ABSORPTION" },
            new() { Key = "thermal_burns", Label = "THERMAL BURNS" },
            new() { Key = "heat_stroke", Label = "HEAT STROKE" },
            new() { Key = "contact_skin_eyes", Label = "CONTACT WITH SKIN / EYES" }
        },
        ManualHandlingRisks =
        {
            new() { Key = "backache", Label = "BACKACHE" },
            new() { Key = "muscle_pain", Label = "MUSCLE PAIN" }
        },
        ExcavationRisks =
        {
            new() { Key = "underground_utilities", Label = "UNDERGROUND UTILITIES" },
            new() { Key = "falling_rocks", Label = "FALLING ROCKS / STONES" },
            new() { Key = "collapse", Label = "COLLAPSE" }
        },
        ElectricalRisks =
        {
            new() { Key = "electrical_shock", Label = "ELECTRICAL SHOCK" },
            new() { Key = "electrocution", Label = "ELECTROCUTION" },
            new() { Key = "arcing", Label = "ARCING" },
            new() { Key = "burn", Label = "BURN" },
            new() { Key = "flash_fire", Label = "FLASH FIRE" }
        },
        ConfinedSpaceRisks =
        {
            new() { Key = "asphyxiation", Label = "ASPHYXIATION / ANOXIA / INTOXICATION" },
            new() { Key = "burial", Label = "BURIAL" }
        },
        OverheadAndDroppedObjectRisks =
        {
            new() { Key = "damage_facilities", Label = "DAMAGE TO FACILITIES" },
            new() { Key = "overhead_cables", Label = "OVERHEAD CABLES / OBSTRUCTIONS" },
            new() { Key = "falling_material", Label = "FALLING MATERIAL" },
            new() { Key = "falling_objects", Label = "FALLING OBJECTS" }
        },
        HeightRisks =
        {
            new() { Key = "vertigo", Label = "VERTIGO" },
            new() { Key = "anxiety", Label = "ANXIETY" },
            new() { Key = "falling_heights", Label = "FALLING FROM HEIGHTS" },
            new() { Key = "suspension_trauma", Label = "SUSPENSION TRAUMA" }
        },
        RadiographicRisks =
        {
            new() { Key = "irradiation", Label = "IRRADIATION" },
            new() { Key = "burns_rt", Label = "BURNS" },
            new() { Key = "nausea_vomiting", Label = "NAUSEA / VOMITING" }
        },
        NoiseRisks =
        {
            new() { Key = "noise_hearing_loss", Label = "NOISE INDUCED HEARING LOSS" }
        }
    };

    private static WorkAuthorizationPreventionMeasuresSectionDto CreatePreventionMeasures() => new()
    {
        TrafficAndOperationalControls =
        {
            new() { Key = "respect_traffic", Label = "RESPECT TRAFFIC, PARKING PLAN AND SPEED LIMITS" },
            new() { Key = "vehicles_good_condition", Label = "APPROPRIATE VEHICLES / MACHINERY IN GOOD CONDITION, REGULATORY INSPECTIONS UP-TO-DATE" },
            new() { Key = "help_circulation", Label = "HELP WITH CIRCULATION" },
            new() { Key = "life_jacket", Label = "WEARING OF LIFE JACKET" }
        },
        ExplosionAndAtexControls =
        {
            new() { Key = "atex_tools", Label = "ATEX TOOLS / EQUIPMENT" },
            new() { Key = "atex_trained", Label = "PERSONNEL TRAINED IN ATEX WORK" },
            new() { Key = "gas_testing_lel_o2", Label = "GAS TESTING (LEL & O₂)" },
            new() { Key = "portable_beacon", Label = "PORTABLE BEACON" },
            new() { Key = "hot_work_permit_atex", Label = "HOT WORK PERMIT" }
        },
        MechanicalControls =
        {
            new() { Key = "safety_goggles", Label = "WEARING OF SAFETY GOGGLES" },
            new() { Key = "cut_resistant_gloves", Label = "WEARING OF CUT-RESISTANT GLOVES" },
            new() { Key = "training_mech", Label = "TRAINING" },
            new() { Key = "authorisation_mech", Label = "AUTHORISATION" },
            new() { Key = "lockout_certificate", Label = "LOCKOUT CERTIFICATE" }
        },
        ChemicalAndPpeControls =
        {
            new() { Key = "msds", Label = "MSDS" },
            new() { Key = "product_identification", Label = "PRODUCT IDENTIFICATION" },
            new() { Key = "appropriate_ppe_chem", Label = "WEARING OF APPROPRIATE PPE" },
            new() { Key = "spill_kit", Label = "SPILL KIT" }
        },
        PressureAndManualHandlingControls =
        {
            new() { Key = "cooling_equipment", Label = "COOLING OF THE EQUIPMENT" },
            new() { Key = "trained_pressurised", Label = "WORKERS TRAINED ON PRESSURISED EQUIPMENT" },
            new() { Key = "movements_posture", Label = "APPROPRIATE MOVEMENTS AND POSTURE" },
            new() { Key = "mechanical_handling", Label = "MECHANICAL HANDLING EQUIPMENT" },
            new() { Key = "worker_assistance", Label = "WORKER ASSISTANCE" },
            new() { Key = "load_weights", Label = "RESPECT FOR REGULATORY LOAD WEIGHTS" }
        },
        ExcavationControls =
        {
            new() { Key = "excavation_permit", Label = "EXCAVATION PERMIT" }
        },
        ElectricalControls =
        {
            new() { Key = "energy_isolation_permit", Label = "ENERGY ISOLATION PERMIT" },
            new() { Key = "lockout_cert_elec", Label = "LOCKOUT CERTIFICATE" },
            new() { Key = "earthing", Label = "EARTHING OF EQUIPMENT" }
        },
        ConfinedSpaceControls =
        {
            new() { Key = "confined_space_permit", Label = "CONFINED SPACE PERMIT" }
        },
        LiftingControls =
        {
            new() { Key = "lifting_permit", Label = "LIFTING PERMIT" },
            new() { Key = "no_walk_under_load", Label = "DO NOT WALK UNDERNEATH A SUSPENDED LOAD" },
            new() { Key = "overhead_networks", Label = "IDENTIFY OVERHEAD NETWORKS" },
            new() { Key = "supervisor_lifting", Label = "SUPERVISOR" }
        },
        WorkingAtHeightsControls =
        {
            new() { Key = "heights_permit", Label = "WORKING AT HEIGHTS PERMIT" },
            new() { Key = "compliant_equipment", Label = "COMPLIANT EQUIPMENT AND IN GOOD CONDITION" },
            new() { Key = "harness", Label = "WEARING OF HARNESS" },
            new() { Key = "fall_arrest_rescue", Label = "FALL ARREST AND RESCUE TRAINING" },
            new() { Key = "first_aider", Label = "FIRST AIDER" }
        },
        CleaningDegassingControls =
        {
            new() { Key = "cleaning_degassing_permit", Label = "CLEANING-DEGASSING PERMIT" },
            new() { Key = "gas_testing_degass", Label = "GAS TESTING (LEL & O₂)" }
        },
        RadiographicControls =
        {
            new() { Key = "radiographic_permit", Label = "RADIOGRAPHIC TESTING PERMIT" },
            new() { Key = "ppe_radiographic", Label = "WEARING OF APPROPRIATE PPE" }
        },
        NoiseControls =
        {
            new() { Key = "hearing_protection", Label = "WEARING OF HEARING PROTECTION" }
        }
    };
}
