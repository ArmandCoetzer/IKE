# Work Authorisation amendments, re–sign-off, and child permits

## Product intent (not yet fully implemented in API/mobile)

When a **signed-off** Work Authorisation (WA) is **edited** and the change is **not** yet covered by a **new client sign-off** (amendment / second signature):

### Default: full standstill

Everything on the job that depends on permit workflow **stops**, until the amended WA is signed off again. That includes (non-exhaustive):

- Requesting **new** child permits (any type).
- Progressing **draft** or **unsigned** child permits that were not already in active, signed-off use.
- Any other permit actions that would extend scope or bypass the amended master (exact list to be enforced in API).

### Sole exception: in-flight signed-off child work

The **only** permits that may **continue** are **child** (non-WA) permits that **already** have **client sign-off** and where **work has already started** under that permit (e.g. job is in progress and that permit row is live: Active/Approved with sign-off, not awaiting sign-off and not draft-only). Technicians may keep working **only** within that already-authorised, already-started scope.

After the amended WA is signed off, **all** permit behaviour returns to normal.

### WA re–sign and audit

- The client must sign off again on the **changed** WA (second signature / amendment), with **audit** (who, when, revision or payload hash).
- PDF / documentation should reflect amendment history if required by your process.

## Why this needs backend state

The mobile app cannot enforce this reliably alone. The API should persist something like:

- `PendingWaAmendmentSignOff` (or equivalent) on the **master WA** when content changes after the last client sign-off.
- Invalidation of the **previous** client signature on the WA until amendment is signed.
- Optional: `WaRevision` / `LastSignedPayloadHash` for clear audit.

Controllers that mutate permits, documents, or job state should check `PendingWaAmendmentSignOff` and allow paths **only** for child permits that satisfy the **in-flight signed-off + work started** rule above; everything else returns a clear error until the WA amendment is signed off.

## Job block / unblock (incident vs coordinator)

**Unblocking a job** (clearing `BlockedReason` after Block job or incident-driven block) remains **web app only**, using the existing coordinator flow (`PATCH .../unblock` with `RequireAssignTechnicians`). **No** mobile unblock endpoint or UI.

---

## Summary

| Situation | Behaviour |
|-----------|-----------|
| WA amended, amendment **not** signed off | Standstill for all permit work **except** continuing already–client-signed-off child permits where work has **already started**. |
| WA amendment signed off | Normal permit workflow resumes. |
| Job blocked (`BlockedReason` set) | Unblock **only** on the web app (unchanged). |
