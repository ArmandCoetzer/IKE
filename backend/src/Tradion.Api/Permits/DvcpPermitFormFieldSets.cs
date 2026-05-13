namespace Tradion.Api.Permits;

/// <summary>Structured form fields appended to every child permit (non-WA). Checklist lines remain separate commitments.</summary>
public static class DvcpPermitFormFieldSets
{
    private static readonly PermitFormFieldDefinition[] General =
    [
        new("work_location", "Exact work location / equipment tag", PermitFormFieldDefinition.TypeText, "General", true),
        new("task_summary", "Task covered by this permit", PermitFormFieldDefinition.TypeTextArea, "General", true),
        new("issuing_authority_name", "Issuing authority name", PermitFormFieldDefinition.TypeText, "Authorities", true),
        new("issuing_authority_role", "Issuing authority role / title", PermitFormFieldDefinition.TypeText, "Authorities", false),
        new("performing_authority_name", "Performing authority name", PermitFormFieldDefinition.TypeText, "Authorities", true),
        new("performing_authority_role", "Performing authority role / title", PermitFormFieldDefinition.TypeText, "Authorities", false),
        new("intended_start_date", "Intended work date", PermitFormFieldDefinition.TypeDate, "General", true),
        new("site_contact", "Site contact / phone", PermitFormFieldDefinition.TypeText, "General", false)
    ];

    public static IReadOnlyList<PermitFormFieldDefinition> ForStableKey(string stableKey) => stableKey switch
    {
        "dvcp.work_authorisation" => Array.Empty<PermitFormFieldDefinition>(),
        "dvcp.high_risk_situations" => Concat(General, HighRisk),
        "dvcp.traffic" => Concat(General, Traffic),
        "dvcp.body_mechanics_tools" => Concat(General, BodyMechanics),
        "dvcp.ppe" => Concat(General, Ppe),
        "dvcp.work_permit_commitments" => Concat(General, WorkPermitCommitments),
        "dvcp.lifting_operations" => Concat(General, Lifting),
        "dvcp.energy_isolation_powered_systems" => Concat(General, EnergyIsolation),
        "dvcp.confined_space_entry" => Concat(General, ConfinedSpace),
        "dvcp.line_of_fire" => Concat(General, LineOfFire),
        "dvcp.work_at_height" => Concat(General, WorkAtHeight),
        "dvcp.hot_work" => Concat(General, HotWork),
        "dvcp.excavation_work" => Concat(General, Excavation),
        _ => Concat(General, [])
    };

    private static readonly PermitFormFieldDefinition[] HighRisk =
    [
        new("supervisor_briefed_name", "Supervisor briefed (name)", PermitFormFieldDefinition.TypeText, "Briefing", true),
        new("non_routine_details", "If non-routine / complex: describe hazards reviewed", PermitFormFieldDefinition.TypeTextArea, "Risk", false)
    ];

    private static readonly PermitFormFieldDefinition[] Traffic =
    [
        new("vehicle_equipment_id", "Vehicle / equipment ID", PermitFormFieldDefinition.TypeText, "Vehicle", false),
        new("journey_plan_ref", "Journey management plan reference", PermitFormFieldDefinition.TypeText, "Planning", false)
    ];

    private static readonly PermitFormFieldDefinition[] BodyMechanics =
    [
        new("tools_listed", "Tools to be used (list)", PermitFormFieldDefinition.TypeTextArea, "Tools", true),
        new("pressure_test_details", "Pressure test details (if applicable)", PermitFormFieldDefinition.TypeTextArea, "Tools", false)
    ];

    private static readonly PermitFormFieldDefinition[] Ppe =
    [
        new("ppe_verified_ok", "PPE inspected and serviceable", PermitFormFieldDefinition.TypeBool, "PPE", true),
        new("specific_ppe", "Task-specific PPE (beyond standard)", PermitFormFieldDefinition.TypeTextArea, "PPE", false)
    ];

    private static readonly PermitFormFieldDefinition[] WorkPermitCommitments =
    [
        new("permit_certificate_refs", "Permit / certificate references on site", PermitFormFieldDefinition.TypeTextArea, "Permits", true),
        new("intervention_point_id", "Point of intervention identified (tag / location)", PermitFormFieldDefinition.TypeText, "Permits", true)
    ];

    private static readonly PermitFormFieldDefinition[] Lifting =
    [
        new("lift_plan_ref", "Lifting plan reference", PermitFormFieldDefinition.TypeText, "Lifting", true),
        new("banksman_name", "Banksman / signal person name", PermitFormFieldDefinition.TypeText, "Lifting", true),
        new("crane_equipment_id", "Lifting equipment ID", PermitFormFieldDefinition.TypeText, "Lifting", false),
        new("load_description", "Load description / weight (if known)", PermitFormFieldDefinition.TypeTextArea, "Lifting", true)
    ];

    private static readonly PermitFormFieldDefinition[] EnergyIsolation =
    [
        new("isolation_certificate_ref", "Isolation certificate / LOTO reference", PermitFormFieldDefinition.TypeText, "Isolation", true),
        new("energy_sources_identified", "Energy & fluid sources identified", PermitFormFieldDefinition.TypeTextArea, "Isolation", true),
        new("isolation_point_tags", "Lock / tag numbers", PermitFormFieldDefinition.TypeTextArea, "Isolation", true)
    ];

    private static readonly PermitFormFieldDefinition[] ConfinedSpace =
    [
        new("confined_space_id", "Confined space ID / description", PermitFormFieldDefinition.TypeText, "Entry", true),
        new("entry_supervisor_name", "Entry supervisor name", PermitFormFieldDefinition.TypeText, "Entry", true),
        new("rescue_plan_ref", "Rescue plan reference", PermitFormFieldDefinition.TypeText, "Entry", true),
        new("entrant_names", "Entrant names", PermitFormFieldDefinition.TypeTextArea, "Entry", true),
        new("gas_test_ref", "Atmosphere test reference / result", PermitFormFieldDefinition.TypeTextArea, "Atmosphere", true),
        new("continuous_monitoring", "Continuous monitoring arranged", PermitFormFieldDefinition.TypeBool, "Atmosphere", true)
    ];

    private static readonly PermitFormFieldDefinition[] LineOfFire =
    [
        new("exclusion_zone_ref", "Barriers / exclusion zone reference", PermitFormFieldDefinition.TypeText, "Zones", true),
        new("suspended_load_activity", "Suspended load activity (describe or N/A)", PermitFormFieldDefinition.TypeTextArea, "Hazards", true)
    ];

    private static readonly PermitFormFieldDefinition[] WorkAtHeight =
    [
        new("height_work_method", "Means of access (scaffold / MEWP / ladder — specify)", PermitFormFieldDefinition.TypeTextArea, "Height", true),
        new("anchor_points_ref", "Harness / anchor points reference", PermitFormFieldDefinition.TypeText, "Height", true),
        new("mewp_pre_use_check", "MEWP pre-use check done (if applicable)", PermitFormFieldDefinition.TypeBool, "Height", false),
        new("fragile_surface_controls", "Fragile surface / roof controls", PermitFormFieldDefinition.TypeTextArea, "Height", false)
    ];

    private static readonly PermitFormFieldDefinition[] HotWork =
    [
        new("hot_work_methods", "Hot work methods (welding, grinding, cutting, etc.)", PermitFormFieldDefinition.TypeTextArea, "Hot work", true),
        new("combustibles_controls", "Combustibles removed / isolated / wetted (describe)", PermitFormFieldDefinition.TypeTextArea, "Hot work", true),
        new("fire_watch_name", "Fire watch assigned (name)", PermitFormFieldDefinition.TypeText, "Hot work", true),
        new("fire_equipment_ok", "Firefighting equipment in place and checked", PermitFormFieldDefinition.TypeBool, "Hot work", true),
        new("gas_testing_done", "Gas / flammable atmosphere testing completed", PermitFormFieldDefinition.TypeBool, "Hot work", true),
        new("gas_test_reference", "Gas test certificate / reading reference", PermitFormFieldDefinition.TypeText, "Hot work", false),
        new("written_authorisation_ref", "Written authorisation reference (if separate)", PermitFormFieldDefinition.TypeText, "Hot work", false)
    ];

    private static readonly PermitFormFieldDefinition[] Excavation =
    [
        new("excavation_certificate_ref", "Excavation certificate reference", PermitFormFieldDefinition.TypeText, "Excavation", true),
        new("underground_services_locate_ref", "Underground services locate / plans reference", PermitFormFieldDefinition.TypeText, "Excavation", true),
        new("shoring_sloping", "Shoring / battering / controls", PermitFormFieldDefinition.TypeTextArea, "Excavation", false),
        new("machinery_kept_clear", "Machinery / spoil kept ≥1 m from edge confirmed", PermitFormFieldDefinition.TypeBool, "Excavation", true)
    ];

    private static IReadOnlyList<PermitFormFieldDefinition> Concat(
        IReadOnlyList<PermitFormFieldDefinition> a,
        PermitFormFieldDefinition[] b) =>
        a.Concat(b).ToList();
}
