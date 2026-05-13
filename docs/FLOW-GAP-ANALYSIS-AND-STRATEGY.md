# Flow Gap Analysis & Strategy Plan

This document compares the described DVCP system flow with the existing Tradion codebase and outlines what needs to be added, fixed, or changed.

---

## 1. Client Flow

### 1.1 Service Request Creation

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Site selection | ✅ Exists | — |
| Equipment ID/serial + visual evidence | ⚠️ Partial | Equipment dropdown exists; ServiceRequestAttachment exists in model but **service-request-add form has no file upload** for visual evidence |
| Description of issue | ✅ Exists | — |
| Priority level | ✅ Exists | — |
| Optional due date | ✅ Exists | — |

**Fixes:**
- Add file upload (attachments) to service request add form for visual evidence of equipment/issue.

### 1.2 Client Capabilities

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Review completed job history | ⚠️ Partial | Job cards list exists; **need client-scoped view of completed jobs** (may already work via company scoping) |
| Monitor equipment on site | ⚠️ Partial | Equipment list exists; **ensure client can view their site equipment** |

### 1.3 Admin: Penalty for Inflated Priority

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Admin can charge penalty fee and reduce priority if client inflates | ❌ Missing | **New feature:** Admin UI to adjust service request priority and optionally add penalty fee/note. |

---

## 2. Main Company (Admin) – Job Setup Process

### 2.1 Start Type Options

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Request (select active request first) | ❌ Wrong flow | **Fix:** "Request" should **select an existing active service request**, not create a new one. Client & site should be prepopulated from the request. |
| Quote (create own) | ✅ Exists | — |
| Quote (existing) | ✅ Exists | — |

### 2.2 Quote Section (when creating own or from request)

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Build quote with materials (select stock, qty, price per item) | ⚠️ Partial | Quote has line items (Labour/Part); **start-new-job only has amount + description**, no line-item/material picker in the wizard |
| Guestimation value only | ✅ Exists | — |
| "Sort price later" checkbox (for urgent) | ❌ Missing | **New:** Checkbox next to amount to defer pricing; allow quote with 0/null amount and flag |
| If no materials, prompt to add stock / request from suppliers | ❌ Missing | **New:** When materials chosen but none in inventory, show link to add parts / create PO to supplier |

### 2.3 Purchase Order

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| We create PO (internal) | ✅ Exists | — |
| Client sends PO (number + file) | ✅ Exists | — |

### 2.4 Job Setup (Equipment, Permits, Technicians)

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Job description | ✅ Exists | — |
| Equipment selection (tools for the job) | ❌ Missing | **New:** JobCard/JobType needs link to Equipment. Equipment = tools used to complete the job (e.g. drill). |
| Equipment → permit requirements | ❌ Missing | **New:** Equipment should have optional PermitTypeId; when equipment selected, job inherits permit requirements (e.g. drill → hot permit). |
| Job priority 1–5 (1=least, 5=most urgent) | ❌ Missing | **New:** JobCard has no Priority. Add `Priority` (int 1–5) and `DueDate` (optional). |
| Job due date (optional) | ❌ Missing | **New:** Add `DueDate` to JobCard. |
| Technicians (1 to many) | ✅ Exists | JobCardAssignment. |
| One technician = Permit Manager | ❌ Missing | **New:** Add `IsPermitManager` (or similar) to JobCardAssignment. Only this technician can manage permits on site (in app). |

### 2.5 Permits

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Permits always required | ⚠️ Contradicts spec | Current: "Permits required" toggle + "Permit type" selector. **Fix:** Remove UI for "permits required" / "permit type" from start-new-job. Treat permits as always required. |
| Permit type specified in digital permits | ⚠️ Partial | PermitType, PermitTemplate, JobPermit exist. **Keep backend model**; remove manual permit-type selection from job setup. |

---

## 3. Progress Report & Financial Threshold

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Progress report (downloadable) | ⚠️ Partial | `GET /api/reports/progress` returns labour hours from invoices. **Ensure downloadable PDF/report exists** for owners. |
| Hours completed | ✅ Exists | Labour hours from invoice line items. |
| Financial threshold progress bar | ✅ Exists | ClientBudget: threshold, spent, work paused, approve continuation. |
| Work ceases when threshold met | ✅ Exists | WorkPaused, ContinuationApprovedAt. |

---

## 4. Job Card – Technician Flow (Flutter App)

The described flow (open jobs by priority, restrict to highest, permits, before/after photos, etc.) is for the **Flutter technician app**. The web system must support:

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Job card visible to technicians | ✅ API exists | JobCardWorkController. |
| No cost/price shown to technicians | ❓ Unknown | **Verify** JobCardWorkDto / app do not expose quote amount, invoice amount to technicians. |
| Permit manager can manage permits only | ❌ Missing | Requires `IsPermitManager` on assignment + app logic. |
| Work Authorisation (master permit) | ❓ Model exists | PermitType/PermitTemplate. **Define** "Work Authorisation" as a specific type and ensure it can drive other permits. |
| Master permit dictates other permits | ❌ Missing | **New:** PermitTemplate or PermitType needs relationship (e.g. "triggers" other permit types based on checklist). |
| Before/after/mid photos | ⚠️ Partial | JobCardDocument, JobPart (old/new part photos). **Need** explicit before-work and after-work photo structure for job site (not just parts). |
| Signature for permits | ❓ Unknown | Check JobPermit / attachments for signature support. |

---

## 5. Invoice Flow

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Invoice from quote selections | ✅ Exists | — |
| Adjust materials/labour based on actuals | ✅ Exists | Line items, confirm-parts flow. |
| Send to client (system + email) | ✅ Exists | — |
| Mark paid | ✅ Exists | — |

---

## 6. Incidents

| Requirement | Current State | Gap |
|-------------|---------------|-----|
| Incidents when things go wrong on site | ⚠️ Partial | IncidentReport exists (JobCardId, Description, Severity, ReportedByUserId). Job card detail shows incidents. |
| Full incident management | ⚠️ Partial | **Verify** technicians/admins can create incidents from job card. **Enhance** as needed (photos, status, resolution). |

---

## 7. Strategy Plan – Execution Order

### Phase 1: Quick Wins & Critical Fixes

1. **Request flow fix (start-new-job)**  
   - Add "Select existing request" for start type "request" instead of creating new.  
   - Prepopulate client/site from selected request.  
   - Wire to quote creation with serviceRequestId.

2. **Permits UI simplification**  
   - Remove "Permits required" and "Permit type" from start-new-job Step 5.  
   - Keep backend as-is; permits treated as always required for job setup.

3. **Service request visual evidence**  
   - Add file upload to service-request-add for attachments (visual evidence).

4. **JobCard: Priority & DueDate**  
   - Add `Priority` (int 1–5) and `DueDate` (optional) to JobCard.  
   - Add to start-new-job Step 6 and job-card-edit.

### Phase 2: Equipment & Permit Logic

5. **Equipment for job (tools)**  
   - Add `JobCardEquipment` (or similar) to link JobCard to Equipment (many-to-many).  
   - Add equipment selection to job setup (Step 6).

6. **Equipment → Permit requirements**  
   - Add `RequiredPermitTypeId` (or similar) to Equipment.  
   - When equipment is selected for a job, aggregate permit requirements onto the job.

7. **Permit manager**  
   - Add `IsPermitManager` to JobCardAssignment.  
   - UI to designate one technician as permit manager when assigning.

### Phase 3: Quote & Pricing

8. **Quote materials in wizard**  
   - Extend start-new-job Step 3 to allow building quote with line items (Labour + Part selection with qty/price).  
   - Reuse or adapt quote-add line-items UI.

9. **"Sort price later" option**  
   - Add flag to Quote (e.g. `DeferPricing`).  
   - Checkbox in wizard; allow amount 0 when checked.

10. **Admin priority penalty**  
    - Admin UI to change service request priority and add penalty note/fee (new field or note).

### Phase 4: Permits Deep Integration (App + Web)

11. **Work Authorisation (master permit)**  
    - Define Work Authorisation as a PermitType.  
    - Model for "master permit triggers other permits" (e.g. PermitType.TriggersPermitTypeIds or PermitTemplate rules).

12. **Before/after site photos**  
    - Extend JobCardDocument or add JobSitePhoto with type (Before/Mid/After).  
    - Ensure job card detail shows these; app can upload.

13. **Progress report download**  
    - Add PDF export for progress report if not present.

14. **Incident management**  
    - Ensure create/edit incident from job card; add photos/attachments if needed.

### Phase 5: Flutter App (Out of Scope for Web)

- Technician app: job list by priority, restrict to highest, permit flow, photos, signatures, etc.  
- Implement in Flutter; consume existing APIs and new fields (IsPermitManager, JobCard.Priority, etc.).

---

## 8. Summary Table

| Area | Status | Priority |
|------|--------|----------|
| Request = select existing (not create) | Fix | High |
| Permits always required (remove UI) | Fix | High |
| Service request attachments/evidence | Add | High |
| JobCard Priority & DueDate | Add | High |
| Equipment for job + permit link | Add | Medium |
| Permit manager flag | Add | Medium |
| Quote materials in wizard | Add | Medium |
| "Sort price later" | Add | Low |
| Admin priority penalty | Add | Low |
| Master permit triggers | Add | Medium (App) |
| Before/after photos structure | Enhance | Medium |
| Progress report PDF | Verify/Add | Low |
| Incident management | Verify/Enhance | Low |

---

*Document generated from flow comparison. Update as implementation progresses.*
