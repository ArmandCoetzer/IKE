# IKE Implementation Checklist

**Recommended commit order.** Each item is a logical, reviewable unit.

---

## Implementation Status (Summary)

### Completed (this session)
- **BUG_AUDIT.md** – Created with all items grouped by severity
- **S-02** – Incident creation restricted to assigned technicians or admins
- **S-03** – File path validation (FilePathHelper) for downloads
- **S-04** – Flutter secure storage for token
- **U-01/F-01** – Blob URL revoke delayed 60s everywhere
- **U-02** – Active permit shown read-only (no dropdown)
- **U-03** – Parts used section removed from job card
- **U-04** – Incident creation UI removed from web
- **U-05** – Start work button removed from web
- **U-06** – PPE and site photos removed from document dropdown
- **U-08** – Quote: hide Create PO when quote has linked PO
- **U-09** – PO status removed (filter, column, buttons)
- **U-11** – IKE colors (red #E31837, charcoal #0A0A0A) and sidebar/topbar
- **UX-01** – returnTo on job card back, quote View PO
- **UX-03** – HTTP error interceptor

### Completed (final pass)
- **S-01** – Technician-only enforcement: document upload, part add require assigned technician or AssignTechnicians
- **W-03** – Budget threshold: API blocks document/part/add and job completion when WorkPaused
- **D-02** – Audit: PermitManagerSet, TechnicianUnassigned, BudgetContinuationApproved
- **F-03** – File streaming for document/part/incident/permit downloads (FileStreamResult)
- **U-10** – LoaderComponent + used in job-cards-list
- **U-07** – returnTo on invoice-detail, job-card-detail, purchase-order-detail, quote-detail
- **A-01** – Permit auto-expire: background job now sets Status = "Expired" when ValidTo passed

### Not yet implemented (larger changes)
- **W-01** – Full permit state machine (Draft → Signed → Active → Expired/Closed)
- **W-02** – "Blocked" status or override for highest-priority job

---

## Phase 1: Security & Permissions

### Commit 1.1: Add technician-work and incident policies
- [ ] Add `RequireTechnicianWork` policy (technician role OR assigned to job)
- [ ] Add `RequireCreateIncident` policy
- [ ] **Files:** `Program.cs`, new `TechnicianWorkAuthorizationHandler` (if needed)
- **PR note:** Restricts incident creation and technician-only actions to proper roles/assignment.

### Commit 1.2: Restrict incident creation to technicians
- [ ] Change incident endpoints in `JobCardWorkController` to `RequireTechnicianWork` or `RequireCreateIncident`
- [ ] Enforce "assigned technician or permit manager" for incident create/resolve
- [ ] **Files:** `JobCardWorkController.cs`
- **PR note:** Incidents can only be created by assigned technicians; web will remove incident UI in Phase 3.

### Commit 1.3: Secure file downloads (path + ownership)
- [ ] Validate `storedPath` is under `uploads/` and has no `..`
- [ ] Add ownership/tenant/job access checks for each file endpoint
- [ ] **Files:** `DocumentsController.cs`, `JobCardWorkController.cs` (document/photo downloads), new `FileAccessService` or similar
- **PR note:** Prevents path traversal and unauthorized file access.

### Commit 1.4: Mobile token in secure storage
- [ ] Add `flutter_secure_storage` dependency
- [ ] Replace SharedPreferences token storage with secure storage in `auth_service.dart`
- [ ] **Files:** `pubspec.yaml`, `auth_service.dart`
- **PR note:** Token no longer stored in plaintext; important for lost devices.

---

## Phase 2: Permit Workflow

### Commit 2.1: Permit state machine (backend)
- [ ] Add permit states: Draft, Signed, Active, Expired, Closed
- [ ] Add transitions and validation
- [ ] **Files:** `JobPermit` model, `JobPermitsController`, migrations
- **PR note:** Permits follow explicit state machine; web becomes read-only for permit state.

### Commit 2.2: Auto-derive active permit on job card
- [ ] Derive active permit from permit state (e.g. first Active, valid)
- [ ] Remove admin upload/pick active permit UI
- [ ] **Files:** `JobCardWorkDto`, job card detail component
- **PR note:** Active permit shown automatically; no manual selection.

---

## Phase 3: Remove Process-Mismatch UI

### Commit 3.1: Remove Start Work button from web
- [ ] Remove "Start work" button and related logic from job card detail
- [ ] **Files:** `job-card-detail.component.html`, `job-card-detail.component.ts`
- **PR note:** Start work is technician-only; web is status dashboard.

### Commit 3.2: Remove PPE/safety proof dropdown from job card
- [ ] Remove PPE document type dropdown and upload from job card
- [ ] **Files:** `job-card-detail.component.html`, `job-card-detail.component.ts`
- **PR note:** PPE proof added from technician app only.

### Commit 3.3: Remove "Parts used" section from job card
- [ ] Remove parts-used UI from job card detail
- [ ] Optionally restrict API for parts-used if needed
- [ ] **Files:** `job-card-detail.component.html`, `job-card-detail.component.ts`, API if applicable
- **PR note:** Parts used removed per process; technicians handle via app.

### Commit 3.4: Remove incident creation from web
- [ ] Remove incident create/resolve UI from job card
- [ ] Incidents remain view-only on web
- [ ] **Files:** `job-card-detail.component.html`, `job-card-detail.component.ts`
- **PR note:** Incidents are technician-reported only; web shows read-only.

---

## Phase 4: Blob Download / Image Fix

### Commit 4.1: Delay blob URL revoke everywhere
- [ ] Replace immediate `URL.revokeObjectURL()` with delayed revoke (30–60s) or skip for preview
- [ ] **Files:** `job-card-detail.component.ts`, `invoice-detail.component.ts`, `service-request-detail.component.ts`, `equipment-detail.component.ts`, `purchase-order-detail.component.ts`, `quote-detail.component.ts`, `users-list.component.ts`, `reports.component.ts`
- **PR note:** Fixes images not showing after download/preview across all detail pages.

---

## Phase 5: ReturnTo / Navigation

### Commit 5.1: Shared returnTo navigation helper
- [ ] Create `NavigationService` or helper with `navigateWithReturn(route, returnTo)`
- [ ] **Files:** New `navigation.service.ts`, update `app.routes.ts` if needed
- **PR note:** Foundation for consistent back navigation.

### Commit 5.2: Propagate returnTo site-wide
- [ ] Add returnTo to PO, Quote, Invoice, Job Card, Service Request flows
- [ ] Ensure PO → back to POs, etc.
- [ ] **Files:** Multiple components, route configs
- **PR note:** Users can return to correct list after drill-down.

---

## Phase 6: Loaders + Error Handling

### Commit 6.1: Shared loader component
- [ ] Create `LoaderComponent` / skeleton component
- [ ] **Files:** New `loader.component.ts`, styles
- **PR note:** Consistent loading UX.

### Commit 6.2: Per-action loading flags
- [ ] Add loading flags for submit buttons, list loads
- [ ] Disable buttons during submit
- [ ] **Files:** Multiple components
- **PR note:** Prevents double-clicks and "frozen" perception.

### Commit 6.3: Global HTTP error interceptor
- [ ] Map API errors to consistent toast/snackbar
- [ ] **Files:** New `http-error.interceptor.ts`, `app.config.ts`
- **PR note:** Uniform error handling across app.

---

## Phase 7: Data / Business Logic

### Commit 7.1: Remove PO statuses
- [ ] Remove status field from PO model or make it internal/unused
- [ ] Remove status from PO UI
- [ ] **Files:** `PurchaseOrder` model, PO components, migrations if needed
- **PR note:** PO statuses removed per requirements.

### Commit 7.2: Quote/PO creation logic (U-08)
- [ ] If Quote has PO, do not ask to create
- [ ] Only if quote linked to job + PO created locally: show upload client PO option
- [ ] **Files:** Quote detail, PO components
- **PR note:** Correct conditional logic for PO creation/upload.

### Commit 7.3: Budget threshold enforcement (API)
- [ ] Block work actions when budget threshold exceeded (except incident/safety)
- [ ] **Files:** `JobCardWorkController`, `JobCardsController`, budget service
- **PR note:** Budget stop enforced server-side.

### Commit 7.4: Audit logging for critical actions
- [ ] Ensure audit logs: permit signed/expired, job started/stopped, incident created/resolved, budget stop, technician assignment
- [ ] **Files:** Controllers, `IAuditService`
- **PR note:** Full audit trail for timeline and compliance.

---

## Phase 8: Branding / Theme

### Commit 8.1: IKE colors + logo
- [ ] Apply Yellow #FDCB00, Charcoal #2C2E33 to web
- [ ] Add logo to header/sidebar
- [ ] **Files:** `styles.scss`, `main-layout`, theme config
- **PR note:** IKE branding applied.

### Commit 8.2: IKE colors + logo (Flutter)
- [ ] Ensure technician app uses IKE colors
- [ ] Add logo to app
- [ ] **Files:** `app.dart`, assets
- **PR note:** Mobile app branded.

---

## Phase 9: Performance (Optional / Follow-up)

### Commit 9.1: Stream file downloads
- [ ] Replace `ReadAllBytesAsync` with `FileStreamResult` for large files
- [ ] **Files:** `DocumentsController`, `JobCardWorkController`, etc.
- **PR note:** Reduces memory usage for large file downloads.

---

## Phase 10: Automation (Optional / Follow-up)

### Commits 10.x: Auto-expire permits, orphan cleanup, timeline, etc.
- [ ] A-01: Auto-expire permits + notification
- [ ] A-02: Orphan upload reconciliation job
- [ ] A-03: Job timeline from audit
- [ ] A-04: API reject completion without required photos
- [ ] A-05: Blocked reasons + admin alerts

---

## Test Additions

For each workflow/state change, add:
- Unit tests for permit state transitions
- Unit tests for job status + budget threshold
- Integration tests for technician-only endpoints (403 when not assigned)
- Integration tests for file download authorization
- Tests for blob revoke timing (if feasible)
