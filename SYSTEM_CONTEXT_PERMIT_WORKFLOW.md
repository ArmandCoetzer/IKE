# IKE Permit Workflow Context (Agreed Rules)

This file captures the currently agreed business rules for WA and child permit lifecycle behavior.

## Core Concepts

- Web and mobile "start" are different phases:
  - Web start/opening flow prepares/coordinates the job.
  - Mobile "Start job" indicates technicians start on-site work.
- WA is the master permit and can remain active while work proceeds.
- WA is excluded from "all permits done" completion gating on mobile.

## Time Rules

- Permit expiry is evaluated by **calendar day cutoff**, not minute-level timestamp.
- "Next day" logic uses South Africa timezone (`Africa/Johannesburg` / Eastern Cape, UTC+2).
- Rollover creation is **lazy**:
  - Triggered on first backend read interaction (GET) that loads job/permit data.

## Duplication Rules

- Duplicate permit rows are allowed only when prior same-type permit is **Expired**.
- Closed/Done alone does not allow duplicates.

## WA Expiry Rules

- If WA is expired and no valid signed WA exists:
  - Mobile enters standstill mode.
  - Everything is blocked except incident create/update and WA recovery actions.
- New WA is auto-created (Draft) on next-day lazy read.
- New WA is a separate historical record visible alongside expired WA.
- WA prefill copies prior WA content but excludes signatures.

## Child Permit Rules

- Existing child permits remain historically tied to earlier WA and are additionally linked to the new WA context.
- Child rollover permit is created only when latest permit of that type is expired and next day is reached.
- Child rollover prefill copies previous checklist/form values; signatures are not copied.

## Visibility Rules

- Mobile:
  - Expired permits are view-only.
  - Standstill messaging should explain recovery path.
- Web:
  - Must remain viewable/usable for admin oversight even during standstill.

## Link Model

- Added many-to-many WA-child linkage entity (`JobPermitMasterLink`) to support dual/historical WA associations.
- Legacy `MasterPermitId` remains for compatibility while link table enables multi-WA visibility.

