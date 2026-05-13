# Permit field catalog (Tradion)

This document lists fields for **Work Authorisation (WA)** and each **DVCP-style child permit**.  
Implementation references:

- WA DTO: `DTOs/WorkAuthorizations/WorkAuthorizationMasterPermitDto.cs` (+ nested DTOs), defaults in `WorkAuthorizationMasterPermitDefaults.cs`.
- Child checklists + structured forms: `Permits/DvcpPermitTemplateCatalog.cs`, `Permits/DvcpPermitFormFieldSets.cs`.
- Storage: WA payload → `JobPermit.ChecklistSnapshotJson` via `WorkAuthorizationsController`. Child checklist → `ChecklistSnapshotJson`; structured form → `FormSnapshotJson`; schema → `PermitTemplate.FormSchemaJson`.

---

## 1. Work Authorisation (master)

| Section | Fields / content |
|--------|-------------------|
| **Root** | `permitGuid`, `workAuthorizationNumber`, `notes`, `createdDateUtc`, `modifiedDateUtc` |
| **Header** | `issueDate`, `validFromDate`, `validToDate`, `validFromTime`, `validToTime`, `numberOfWorkers`, `siteName`, `scopeOfWorks`, `atexZone` |
| **Location & task** | `contractorCompany`, `locationOfOperation`, `taskDescription` |
| **Associated permits** | `isProject`, `isMaintenance`, `preventionPlanReferenceNumber`, `safetyConditionCompleted`, `permits[]` (`permitKey`, `permitLabel`, `isRequired`, `referenceNumber`, `notes`) |
| **Interference** | `fuelDeliveryReceiptScheduledAt`, `hasOtherWorkPlannedForDay`, `otherWorkPlannedForDayDetails`, `otherWorkPlannedForDayReferenceNumber`, `hasPresenceOfGasCylindersOrBarrels`, `presenceOfGasCylindersOrBarrelsDetails`, `hasOtherNearbyWorkPlanned`, `otherNearbyWorkPlannedDetails`, `otherNearbyWorkReferenceNumber` |
| **Compulsory safety measures** | `personnelInformed`, `mobilePhonesCamerasEtcSwitchedOff`, `closureOfStation`, `hotWorkRestrictedInClassifiedAreas`, `distributionStoppagePartial`, `distributionStoppageTotal`, `protectiveClothingRequired`, `hearingProtectionRequired`, `hardHatRequired`, `gogglesOrFaceShieldRequired`, `visibilityVestRequired`, `steelToeCapShoesRequired`, `highVisibilityCoverallRequired`, `dustMaskRequired`, `glovesRequired`, `additionalNotes` |
| **Nature of works** | Toggle lists: `movementCirculationOptions`, `hotWorkOptions`, `liftingOptions`, `workAtHeightsOptions`; toggles with comments: `workInExplosionRiskZone`, `equipmentTools`, `dangerousMachineryWork`, `hazardousChemicalProducts`, `extremeTemperatureWork`, `pressurisedEquipmentUse`, `manualHandling`, `excavationWork`, `electricalWork` (+ `electricalWorkLv`, `electricalWorkHv`), `confinedAtmosphereWork`, `drainingRinsingWork`, `cleaningDegassingWork`, `radiographicTestingWork`, `noisyWork`, `otherWork`; `manualHandlingTypesAndWeight` |
| **Nature of risks** | Toggle lists: traffic, fire/explosion, mechanical, chemical/thermal, manual handling, excavation, electrical, confined space, overhead/dropped object, height, radiographic, noise; `otherRisksNotes` |
| **Prevention measures** | `activitiesHaltedCompletely`, `activitiesHaltedPartially`, `activitiesHaltedSpecify`; control toggle lists (traffic/ATEX/mechanical/chemical/PPE/pressure/excavation/electrical/confined/lifting/heights/cleaning-degassing/radiographic/noise); `otherPreventionMeasuresNotes` |
| **Declaration** | `declarationText`; signatures: `issuingAuthority`, `performingAuthority`, `siteAcknowledgement` (`name`, `role`, `signedDateTime`, `signatureImageUrl`, `signatureImageBase64`) |
| **Revalidations** | list: `date`, `timeFrom`, `timeTo`, issuing/performing names & signature URLs |
| **Handback** | list: `date`, `worksCompleted`, issuing/performing names & signature URLs |
| **Withdrawal** | `isWithdrawn`, `scopeOfWorkChanges`, `permitRulesViolation`, `accidentOccurrence`, `issuingOrPerformingAuthorityNotOnSite`, `otherReason`, `notes` |
| **Derived required permits** | `permitKey`, `permitLabel`, `isRequired`, `reason` (computed / rules output) |

**Mobile WA UI** (`work_authorization_permit_screen.dart`) covers a **subset** of the full DTO (wizard-style). The **API accepts the full** `WorkAuthorizationMasterPermitDto` shape on save; use the web app or extend the Flutter wizard to expose every toggle list if you need parity on device.

---

## 2. Child permits (structured form + commitment checklist)

Each child type has:

1. **Checklist** — “My commitment to safety” lines (`ChecklistJson` / `checklistItems` in API). All must be checked before sign-off.
2. **Structured form** — `FormSchemaJson` / `formFields` + `formValues` (see `DvcpPermitFormFieldSets` for ids).

### 2.1 High-Risk Situations

- **Checklist ids:** `hrs_1` … `hrs_5` (see catalog).
- **Form ids:** general block + `supervisor_briefed_name`, `non_routine_details`.

### 2.2 Traffic

- **Checklist:** `trf_1` … `trf_7`.
- **Form:** general + `vehicle_equipment_id`, `journey_plan_ref`.

### 2.3 Body Mechanics & Tools

- **Checklist:** `bmt_1` … `bmt_5`.
- **Form:** general + `tools_listed`, `pressure_test_details`.

### 2.4 Personal Protective Equipment (PPE)

- **Checklist:** `ppe_1` … `ppe_4`.
- **Form:** general + `ppe_verified_ok` (bool), `specific_ppe`.

### 2.5 Work Permit Commitments

- **Checklist:** `wpc_1` … `wpc_6`.
- **Form:** general + `permit_certificate_refs`, `intervention_point_id`.

### 2.6 Lifting Operations

- **Checklist:** `lft_1` … `lft_5`.
- **Form:** general + `lift_plan_ref`, `banksman_name`, `crane_equipment_id`, `load_description`.

### 2.7 Energy Isolation (Powered Systems)

- **Checklist:** `enp_1` … `enp_7`.
- **Form:** general + `isolation_certificate_ref`, `energy_sources_identified`, `isolation_point_tags`.

### 2.8 Confined Space Entry

- **Checklist:** `cse_1` … `cse_7`.
- **Form:** general + `confined_space_id`, `entry_supervisor_name`, `rescue_plan_ref`, `entrant_names`, `gas_test_ref`, `continuous_monitoring` (bool).

### 2.9 Line of Fire

- **Checklist:** `lof_1` … `lof_5`.
- **Form:** general + `exclusion_zone_ref`, `suspended_load_activity`.

### 2.10 Work at Height

- **Checklist:** `wah_1` … `wah_7`.
- **Form:** general + `height_work_method`, `anchor_points_ref`, `mewp_pre_use_check` (bool), `fragile_surface_controls`.

### 2.11 Hot Work

- **Checklist:** `htw_1` … `htw_6`.
- **Form:** general + `hot_work_methods`, `combustibles_controls`, `fire_watch_name`, `fire_equipment_ok` (bool), `gas_testing_done` (bool), `gas_test_reference`, `written_authorisation_ref`.

### 2.12 Excavation Work

- **Checklist:** `exc_1` … `exc_5`.
- **Form:** general + `excavation_certificate_ref`, `underground_services_locate_ref`, `shoring_sloping`, `machinery_kept_clear` (bool).

### General block (all child types)

Shared field ids: `work_location`, `task_summary`, `issuing_authority_name`, `issuing_authority_role`, `performing_authority_name`, `performing_authority_role`, `intended_start_date`, `site_contact`.

---

## 3. API: child permit sign-off

`PATCH /api/jobpermits/{id}/checklist` with body:

```json
{
  "items": [ { "id": "htw_1", "label": "…", "checked": true } ],
  "form": { "work_location": "…", "fire_watch_name": "…", "gas_testing_done": "true" }
}
```

Forbidden for Work Authorisation rows (use `/api/work-authorizations/master-permit/...`).

---

## 4. Flutter

- **WA:** `WorkAuthorizationPermitScreen`.
- **Child:** `ChildPermitFormScreen` (menu on permit tile: **Fill work permit & commitments**).
