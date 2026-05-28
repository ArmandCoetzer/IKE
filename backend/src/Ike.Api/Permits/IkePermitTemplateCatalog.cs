namespace Ike.Api.Permits;

/// <summary>
/// Thirteen permit definitions: Work Authorisation (master) plus twelve IKE safety-rule templates with checklists.
/// </summary>
public static class IkePermitTemplateCatalog
{
    public static IReadOnlyList<IPermitCatalogDefinition> All { get; } = Build().ToList();

    private static IEnumerable<IPermitCatalogDefinition> Build()
    {
        yield return new Def(
            "ike.work_authorisation",
            "Work Authorisation",
            "Master permit — full Work Authorisation form (structured JSON).",
            isWorkAuthorisation: true,
            usesWaForm: true,
            lines: Array.Empty<PermitChecklistLine>(),
            formFields: IkePermitFormFieldSets.ForStableKey("ike.work_authorisation"));

        yield return new Def(
            "ike.high_risk_situations",
            "High-Risk Situations",
            "I avoid high-risk situations.",
            lines:
            [
                new("hrs_1", "I do not smoke or vape outside designated areas."),
                new("hrs_2", "I do not work or drive under the influence of alcohol or drugs."),
                new("hrs_3", "I secure the downgraded situation and report it to my supervisor."),
                new("hrs_4", "I know the risks before executing a non-routine or complex operation."),
                new("hrs_5", "I respect the operating instructions for shutting down and starting up equipment and units.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.high_risk_situations"));

        yield return new Def(
            "ike.traffic",
            "Traffic",
            "I follow the safety rules when I drive, ride a bike or walk.",
            lines:
            [
                new("trf_1", "I check the condition of my vehicle before use."),
                new("trf_2", "I always wear a seatbelt."),
                new("trf_3", "I do not exceed the speed limit and adapt my driving to road conditions."),
                new("trf_4", "I do not use any communication system while driving, such as phone, walkie-talkie and radio, even with hands-free kit."),
                new("trf_5", "I respect the authorised driving time and the journey management plan."),
                new("trf_6", "I use the lanes dedicated to pedestrians and cyclists accordingly."),
                new("trf_7", "I hold handrails when taking the stairs.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.traffic"));

        yield return new Def(
            "ike.body_mechanics_tools",
            "Body Mechanics & Tools",
            "I handle tools safely.",
            lines:
            [
                new("bmt_1", "I check that my tool is the one specified in the work permit or operating instruction."),
                new("bmt_2", "I check that my tool is suitable for the task and work area."),
                new("bmt_3", "I check that my tool is in good condition."),
                new("bmt_4", "I use the tools, including those for pressure tests, in line with the manufacturer's specified design limits."),
                new("bmt_5", "I position my body to minimize excessive strain.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.body_mechanics_tools"));

        yield return new Def(
            "ike.ppe",
            "Personal Protective Equipment (PPE)",
            "I wear the required PPE.",
            lines:
            [
                new("ppe_1", "I check that my PPE are in good condition before use."),
                new("ppe_2", "I wear my helmet with the chin strap fastened."),
                new("ppe_3", "I wear the PPE adapted for the task and the area in which I am working."),
                new("ppe_4", "I wear a life jacket whenever required.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.ppe"));

        yield return new Def(
            "ike.work_permit_commitments",
            "Work Permit Commitments",
            "I work with a valid permit.",
            lines:
            [
                new("wpc_1", "I have checked the permit and associated certificates."),
                new("wpc_2", "I am qualified and authorised to perform the work."),
                new("wpc_3", "I understand the work permit."),
                new("wpc_4", "I ensure that the point of intervention is identified."),
                new("wpc_5", "I have checked that the safety conditions are met to start the work."),
                new("wpc_6", "I stop and reassess the risks if conditions change and refer to my supervisor.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.work_permit_commitments"));

        yield return new Def(
            "ike.lifting_operations",
            "Lifting Operations",
            "I follow the lifting plan.",
            lines:
            [
                new("lft_1", "I establish barriers and exclusion zones."),
                new("lft_2", "I check that the lifting equipment has been inspected, is in good condition and fit for purpose."),
                new("lft_3", "I only operate equipment that I am qualified to use."),
                new("lft_4", "I check that the load is securely slung and bundled and I control the load in motion."),
                new("lft_5", "I ensure that a qualified banksman is present for the lifting operation.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.lifting_operations"));

        yield return new Def(
            "ike.energy_isolation_powered_systems",
            "Energy Isolation (Powered Systems)",
            "I check the isolation and the absence of energy and fluids before any intervention.",
            lines:
            [
                new("enp_1", "I have a permit to work and a powered system isolation certificate."),
                new("enp_2", "I have identified all energy and fluid sources."),
                new("enp_3", "I respect the isolation plan."),
                new("enp_4", "I confirm that energy and fluid sources have been isolated, locked, and tagged."),
                new("enp_5", "I ensure that there is no energy and fluid supply."),
                new("enp_6", "I ensure that there is no residual or accumulated energy and fluid."),
                new("enp_7", "I ensure that the work is completed and check the removal of isolation before starting up.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.energy_isolation_powered_systems"));

        yield return new Def(
            "ike.confined_space_entry",
            "Confined Space Entry",
            "I obtain authorisation before entering a confined space.",
            lines:
            [
                new("cse_1", "I have a work permit and a confined space entry certificate."),
                new("cse_2", "I ensure all energy and fluid sources are isolated."),
                new("cse_3", "I check and use respiratory protection equipment when required."),
                new("cse_4", "I confirm a rescue plan is in place."),
                new("cse_5", "I confirm the atmosphere has been tested prior to intervention and that it is monitored."),
                new("cse_6", "I confirm there is supervision for entry/exit and for alerting."),
                new("cse_7", "I obtain authorisation to enter.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.confined_space_entry"));

        yield return new Def(
            "ike.line_of_fire",
            "Line of Fire",
            "I keep myself and others out of the line of fire.",
            lines:
            [
                new("lof_1", "I never position myself under a suspended load."),
                new("lof_2", "I position myself to avoid moving objects, vehicles, pressure release, and dropped objects."),
                new("lof_3", "I establish barriers and exclusion zones."),
                new("lof_4", "I take action to secure loose objects."),
                new("lof_5", "I respect barriers and exclusion zones.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.line_of_fire"));

        yield return new Def(
            "ike.work_at_height",
            "Work at Height",
            "I protect myself against a fall when working at height >= 1.5m.",
            lines:
            [
                new("wah_1", "I inspect my harness, lanyard and lifeline before use."),
                new("wah_2", "I secure tools and materials to prevent dropped objects."),
                new("wah_3", "I wear a harness and tie off to approved anchor points as per the work permit."),
                new("wah_4", "I use scaffolding fit for purpose and approved."),
                new("wah_5", "I respect the minimum safety distance when working near power lines."),
                new("wah_6", "I ensure the integrity of roofs (storage tanks, buildings, canopies...) before work starts and that appropriate fall protection has been installed for fragile areas."),
                new("wah_7", "I only move a Mobile Elevating Work Platform (MEWP) in its low position.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.work_at_height"));

        yield return new Def(
            "ike.hot_work",
            "Hot Work",
            "I avoid hot work whenever possible.",
            lines:
            [
                new("htw_1", "I have a hot work permit."),
                new("htw_2", "I identify flammable substances and ignition sources."),
                new("htw_3", "I ensure the absence of flammable substances or their isolation before starting any hot work."),
                new("htw_4", "I obtain a written authorisation before starting any hot work."),
                new("htw_5", "In a hazardous area, I confirm the absence of gas has been tested."),
                new("htw_6", "In a hazardous area, I confirm the absence of gas will be continuously monitored.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.hot_work"));

        yield return new Def(
            "ike.excavation_work",
            "Excavation Work",
            "I secure excavation areas.",
            lines:
            [
                new("exc_1", "I have a work permit and an excavation certificate."),
                new("exc_2", "I confirm that the excavation area is clearly marked off."),
                new("exc_3", "I stay alert to the location of underground structures and networks."),
                new("exc_4", "I position machinery and extracted material at least one meter away from the excavation area."),
                new("exc_5", "I only enter an excavation deeper than 1.3m if the access is secured.")
            ],
            formFields: IkePermitFormFieldSets.ForStableKey("ike.excavation_work"));
    }

    private sealed class Def : IPermitCatalogDefinition
    {
        public Def(string stableKey, string name, string? description, IReadOnlyList<PermitChecklistLine> lines,
            bool isWorkAuthorisation = false, bool usesWaForm = false,
            IReadOnlyList<PermitFormFieldDefinition>? formFields = null)
        {
            StableKey = stableKey;
            Name = name;
            Description = description;
            IsWorkAuthorisation = isWorkAuthorisation;
            UsesStructuredWorkAuthorisationForm = usesWaForm;
            ChecklistLines = lines;
            FormFields = formFields ?? Array.Empty<PermitFormFieldDefinition>();
        }

        public string StableKey { get; }
        public string Name { get; }
        public string? Description { get; }
        public bool IsWorkAuthorisation { get; }
        public bool UsesStructuredWorkAuthorisationForm { get; }
        public IReadOnlyList<PermitChecklistLine> ChecklistLines { get; }
        public IReadOnlyList<PermitFormFieldDefinition> FormFields { get; }
    }
}
