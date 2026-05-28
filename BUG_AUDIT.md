# IKE Bug Audit

**System:** Angular web + .NET API + Flutter technician app  
**Date:** 2025  
**Scope:** Security, workflow, functional bugs, UX, performance, automation

---

## 1. Security

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **S-01** | Technician-only actions not enforced server-side | UI hides actions but API allows them (bypass via Postman) | Enforce on API by role/permission AND job assignment |
| **S-02** | Incident creation authorized with `RequireViewJobCards` (too broad) | `JobCardWorkController` incident endpoints use ViewJobCards | Introduce `RequireCreateIncident` / `RequireTechnicianWork`; enforce "must be assigned technician (or permit manager)" |
| **S-03** | File downloads vulnerable to path issues / overexposure | Controllers use `Path.Combine(_env.ContentRootPath, storedPath)` + `ReadAllBytesAsync`; no validation of path traversal or ownership | Enforce files under known root (e.g. `uploads/`), strip `..`, store normalized relative paths, validate ownership/tenant/job access |
| **S-04** | Mobile token stored in `shared_preferences` (plaintext) | `auth_service.dart` uses SharedPreferences | Use `flutter_secure_storage` (Keychain/Keystore) for token storage |

---

## 2. Workflow / Process Mismatch

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **W-01** | Permits not a state machine | Ad-hoc fields (active permit, attachments, valid from/to) with no hard transitions | Implement explicit permit states + transitions: Draft → Signed → Active → Expired/Closed; web read-only |
| **W-02** | "Highest priority job only" impractical without Blocked/Override | Real life: top job may be blocked (client not on site, missing permits, parts) | Add "Blocked" state with reason code OR allow override with audit log + admin notification |
| **W-03** | Budget threshold stop not enforced at API | Only UI blocks; technicians can still submit work/photos/parts via API | Server-side checks that block work actions when threshold exceeded (except incident/safety) |
| **W-04** | Admin web has technician workflow controls | Start work, permit selection, PPE proof on web conflicts with technician-driven process | Remove from web; make web a "status dashboard" driven by technician events |
| **U-02** | Job card should show active permit automatically | Web lets admin pick/set active permit; conflicts with technician-driven process | Auto-derive active permit; no upload prompt/dropdown on web |
| **U-05** | "Start work" button shown on job card (admin) | Must be technician-driven only | Remove from web |
| **U-06** | PPE/safety proof dropdown on job card | PPE proof added from technician app | Remove from web |

---

## 3. Functional Bugs

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **U-01** / **F-01** | Images on web not showing after download | `URL.revokeObjectURL()` called immediately or too soon after opening blobs | Delay revoke (30–60s), or don't revoke for preview flows |
| **F-02** | Re-fetch after every action = race conditions + flicker | Job-card detail repeatedly calls `get(id)`; SignalR also triggers refresh | Prefer endpoint returns updated DTO, or local state update + debounced refresh |
| **F-03** | API uses `ReadAllBytesAsync` for downloads | Memory risk for large files | Stream files (`FileStreamResult`) + set proper content types |
| **F-04** | Multiple upload flows inconsistent | Documents vs documents/upload vs incidents/with-photos; mismatched validation | Consolidate or call shared service methods |

**Affected blob revoke files:**
- `job-card-detail.component.ts`
- `invoice-detail.component.ts`
- `service-request-detail.component.ts`
- `equipment-detail.component.ts`
- `purchase-order-detail.component.ts`
- `quote-detail.component.ts`
- `users-list.component.ts`
- `reports.component.ts`

---

## 4. UX / Navigation

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **U-07** / **UX-01** | No consistent `returnTo` pattern site-wide | Causes "lost in flow" and wrong edits | Shared navigation helper + always propagate `returnTo` |
| **U-10** / **UX-02** | Loading experience inconsistent | Spinners, disabled buttons, list skeletons vary | One loader component + per-action loading flags |
| **UX-03** | Error handling not uniform | Many `subscribe({ error: ... })` show generic or no errors | Global HTTP interceptor mapping API errors to consistent UX |

---

## 5. Data / Modeling

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **U-03** | "Parts used" section on job card | Web + API support adding parts; conflicts with process | Remove from job card |
| **U-04** | Incident reporting shown on web | Incidents must be technician-reported only | Remove incident creation from web; restrict API |
| **U-09** | Purchase order statuses | Status field exists on PO model | Remove statuses from PO |
| **U-08** | Quote PO creation logic | If Quote has PO, should not ask to create; only if quote linked to job + PO created locally should upload client PO option appear | Fix conditional logic |
| **D-01** | Role-specific DTO leakage | Technicians must not see pricing | Separate DTOs: TechnicianJobDto vs AdminJobDto vs ClientJobDto |
| **D-02** | Audit logging gaps | Critical actions not all logged | Log: permit signed/expired, job started/stopped, incident created/resolved, budget stop, technician assignment changes |

---

## 6. Performance

| ID | Issue | Cause | Fix |
|----|-------|-------|-----|
| **F-03** | `ReadAllBytesAsync` for file downloads | Large files load into RAM | Stream files instead |

---

## 7. Automation

| ID | Issue | Fix |
|----|-------|-----|
| **A-01** | Auto-expire permits + push notification | When permit hits expiry, auto mark expired, block work, notify technician, update web |
| **A-02** | Orphan upload reconciliation | Background job: scan DB for missing files + disk for orphan files → report/cleanup |
| **A-03** | Auto-generate job timeline from audit events | Build timeline view from audit log |
| **A-04** | Auto-enforce required before/after photos | API reject job completion without required evidence |
| **A-05** | Auto "blocked" reasons + admin alerts | If top priority job blocked, notify admin and allow override |

---

## 8. Branding / Theme

| ID | Issue | Fix |
|----|-------|-----|
| **U-11** | Colors and logo | Apply IKE color scheme (red #E31837, charcoal #0A0A0A) + add logo throughout web + mobile |

---

## Summary by Priority

| Priority | Category | Count |
|----------|----------|-------|
| P0 | Security | 4 |
| P1 | Workflow/Process | 7 |
| P2 | Functional Bugs | 4 |
| P3 | UX/Navigation | 3 |
| P4 | Data/Modeling | 6 |
| P5 | Performance | 1 |
| P6 | Automation | 5 |
| P7 | Branding | 1 |
