# Start New Job – Resilience & Flow

## Problems you raised

1. **State lost on refresh/close** – Halfway through (e.g. quote created, then refresh), the wizard state is lost. You can’t resume, and the quote you just created can’t be “reused” in “Use existing quote” because it’s already linked (e.g. to a PO created in the same run) or the UI doesn’t show it.
2. **Quote “already linked”** – If the process stops after creating quote (or quote + PO) but before creating the job card, the quote/PO exist but aren’t tied to any job card. Later, “Use existing quote” may hide that quote (e.g. we filter to quotes without a PO), so you’re stuck.
3. **Send quote to client** – After creating a quote, the option to send the quote to the client should be available (API and quote-detail already support this; we need it visible in the flow).
4. **PO source unknown at quote time** – You don’t always know at quote-creation time whether the client will send a PO or you will create one. Forcing that decision in step 4 before the quote is sent can be too early.
5. **Don’t be overly hasty** – The process should support pausing (e.g. send quote, decide PO later) and ensure everything is linked before moving on.

## Current bug (wizard never links quote/PO to job card)

- In the wizard we: create Quote → create PO (with `quoteId`) → create JobCard (with only `siteId`, `serviceRequestId`, `jobTypeId`).
- We **never** set `PurchaseOrder.JobCardId` or `Quote.JobCardId`. So the job card created at the end is **not** linked to the quote or PO.
- The job card detail API finds quote only via `ServiceRequestId` and finds POs via `po.JobCardId == jobCard.Id`. So after the wizard, the new job card shows **no** quote and **no** linked POs.
- So today, even when the wizard “succeeds”, the links are wrong. This needs to be fixed regardless of the resilience work.

## Your idea: create an “empty” job card early

**I agree with the direction.** Benefits:

- **Single anchor** – One entity (the job card) from the moment you commit to client + site. Everything else (quote, PO, request) links to it.
- **Resumable** – If the browser closes or the page refreshes, you can go to Job cards, find the in-progress one (e.g. status “Draft” or “Setup”), and continue from there (e.g. “Complete setup” or a “Resume start-new-job” flow).
- **No orphans** – Quote and PO are created with `jobCardId` (or linked to the job card as soon as they exist), so nothing is left unlinked.
- **Slower, safer flow** – We can allow “send quote first, add PO later” by having a “Decide later” option for PO and letting users add/link a PO from the job card page when the client responds.

**Things to decide:**

- **When to create the job card**  
  - Option A: At the end of **step 1** (client + site). Then steps 2–5 “fill in” that job card (quote, request, PO, job type).  
  - Option B: At the end of **step 2** (after “how did this start?”). Then we have a job card before we create the quote, so we can pass `jobCardId` when creating the quote and PO.
- **Status for “setup” job cards**  
  - e.g. a status like **“Draft”** or **“Setup”** so they’re easy to filter and don’t mix with “Open” work. When setup is complete (e.g. quote + PO in place, user clicks “Start job” or similar), set status to “Open”.
- **Resume UX**  
  - Either: **URL-based** – e.g. `/start-new-job?jobCardId=...` so the wizard loads that job card and continues from the right step.  
  - Or: **Job card page** – “This job is still in setup. Complete setup (link quote, PO, etc.)” with links/actions to add quote, PO, or “Resume start-new-job wizard” for that job card.

## Suggested direction (concrete)

1. **Fix the link bug first**  
   When the wizard creates the job card, it must:
   - Update the PO (created in step 4) to set `JobCardId` (backend must allow this, e.g. in PO update or a dedicated “link to job card”).
   - Update the Quote to set `JobCardId` (backend must allow this).  
   And the job card detail API should resolve the quote for that job card by **Quote.JobCardId** (and keep existing ServiceRequest-based resolution for backward compatibility).

2. **Anchor job card early (recommended: at end of step 1)**  
   - After step 1, create a job card with only `siteId` (and client implied by site), status **“Draft”** (or “Setup”), no quote, no PO, no service request.  
   - Store `jobCardId` in the wizard state and (optionally) in the URL: `/start-new-job?jobCardId=...`.  
   - Steps 2–5: when creating request/quote/PO, pass this `jobCardId` so they’re linked immediately.  
   - Step 5 “Create job card & finish” becomes “Complete setup” (set status to “Open”, set job type, then redirect to job card).

3. **Persist wizard state for refresh**  
   - Put minimal state in the URL (`jobCardId`, `step`, maybe `clientId`, `siteId`) or in `sessionStorage` so a refresh can restore the wizard and show “Resume from step X” or redirect to the job card if it already exists and is in Draft.

4. **“Decide PO later”**  
   - In step 4, add a third option: **“Decide later (e.g. after sending quote to client)”**.  
   - If selected: don’t create a PO in the wizard; go to step 5 and create/complete the job card. From the job card page, user can “Add purchase order” when they know (client sent PO vs we create one).

5. **Send quote to client**  
   - After creating a quote in step 3, show an option: **“Send quote to client”** (opens email/preview or uses existing quote send API).  
   - Ensure quote detail page has a clear “Send to client” action if it doesn’t already.

6. **Resume when browser closed**  
   - If the user closed the browser after a quote (or PO) was created but no job card yet, we have two cases:
     - **With “job card first”**: The job card was created in step 1, so when they come back they can go to Job cards → filter Draft → open that job card and “Complete setup” (or resume wizard with `?jobCardId=...`). Quote/PO created in the same session would already be linked to that job card.  
     - **Without “job card first”**: We could allow “Use existing quote” to include quotes that already have a PO but **no** `JobCardId`, and when continuing we create the job card and then link that quote (and its PO) to the new job card. That requires backend support to set `Quote.JobCardId` and `PurchaseOrder.JobCardId` after creation.

## Summary

- **Fixing the current link bug** is necessary so that the job card created by the wizard actually shows its quote and POs.
- **Creating an empty/draft job card at the end of step 1** is a solid way to make the process resumable and avoid orphaned quotes/POs.
- **“Decide PO later”** and **“Send quote to client”** address real flow (not knowing PO source until after the quote is sent).
- **State in URL or sessionStorage** improves resilience on refresh; **job card first** improves resilience on browser close.

If you’re happy with this direction, next steps are: (1) backend support to link quote/PO to job card (create and update); (2) job card created at end of step 1 with Draft status; (3) wizard passes `jobCardId` when creating quote/PO; (4) step 4 “Decide later”; (5) “Send quote to client” in wizard + quote detail; (6) optional URL/session state for resume.
