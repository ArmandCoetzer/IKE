# Optional Features – Implementation Summary

## Completed (this pass)

### 1. Email with optional PDF attachment
- **Backend:** `IEmailService.SendQuoteToClientAsync` and `SendInvoiceToClientAsync` now take `bool attachPdf = false`.
- **EmailService** injects `IDocumentService`, generates Quote/Invoice PDF when `attachPdf` is true, and attaches it to the email.
- **API:** `POST api/quotes/{id}/send-email` and `POST api/invoices/{id}/send-email` support `?attachPdf=true` (default true).

### 2. Payment reminder – scheduled job
- **Backend:** `PaymentReminderHostedService` (BackgroundService) runs every 24 hours.
- For each **Sent** invoice with `DueDate < today`, it calls `IEmailService.SendPaymentReminderAsync(id)`.
- Registered in **Program.cs** with `AddHostedService<PaymentReminderHostedService>()`.

### 3. Upload PO to job – backend
- **SubmitDocumentRequest** has optional `PurchaseOrderId`.
- **JobCardDocumentDto** has `PurchaseOrderId` and `PurchaseOrderNumber`.
- **JobCardWorkController.SubmitDocument** sets `doc.PurchaseOrderId = request.PurchaseOrderId`, validates PO exists and (if linked) belongs to the same job.
- **GetJob** includes `PurchaseOrder` on documents and maps `PurchaseOrderNumber`.

### 4. Inventory/Parts – backend
- **Model:** `Part` (Id, Name, Description, PartNumber, Quantity, ReorderLevel, SupplierId, Unit, CreatedAt, UpdatedAt).
- **DbContext:** `DbSet<Part>`, configuration with FK to Supplier (SetNull on delete).
- **Migration:** `20260215000000_AddPartsInventory.cs` creates **Parts** table and IX_Parts_SupplierId.
- **DTOs:** `PartDto` (includes `IsLowStock`), `CreatePartRequest`, `UpdatePartRequest`.
- **PartsController:** List (optional `?lowStockOnly=true`), Get, Create, Update, Delete. Policies: `RequireViewPurchaseOrders` / `RequireManagePurchaseOrders`.

### 5. Frontend – Parts service
- **`frontend/tradion-web/src/app/core/services/parts.service.ts`** – `list(lowStockOnly?)`, `get`, `create`, `update`, `delete`.

---

## What you still need to do

### A. Apply Parts migration
If design-time DbContext is fixed (e.g. Jwt:Key in env or launchSettings):
```bash
cd backend/src/Tradion.Api
dotnet ef database update
```
If the migration fails because **Suppliers** table is missing, create it first (e.g. ensure the migration that creates Suppliers has been applied).

### B. Suppliers in nav
In your **layout/sidebar** (e.g. `shared/layout/layout.component.html`), under **Purchase orders**, add:
- **Suppliers** – link to `/suppliers` (same permission as POs, e.g. `ViewPurchaseOrders`).

### C. Suppliers list / detail / form pages
- **Routes:** `/suppliers`, `/suppliers/new`, `/suppliers/:id`, `/suppliers/:id/edit`.
- **Components:** Supplier list (table with name, email, link to detail), Supplier detail (view + edit button), Supplier form (create/edit with name, email, phone, contactPerson).
- Use existing **SuppliersService** (`list()`, `get(id)`); add `create`, `update` if not already there and backend supports it.

### D. Inventory/Parts pages
- **Routes:** `/parts` (or `/inventory`), `/parts/new`, `/parts/:id`, `/parts/:id/edit`.
- **List:** Use `PartsService.list()`; optional filter “Low stock only” via `list(true)`.
- **Detail:** Show part name, quantity, reorder level, low-stock badge, supplier (link), unit.
- **Form:** Create/edit with name, description, part number, quantity, reorder level, supplier (dropdown from SuppliersService.list()), unit.

### E. Upload PO to job – frontend (job card work / documents)
Wherever job **documents** are uploaded (e.g. job card work screen):
1. Add an **“Upload client PO”** (or “Add document”) action.
2. **Document type:** `ClientPO` (or allow user to choose “Client PO”).
3. **File:** Use existing upload API (e.g. `entityType=JobCardDocument`, `entityId=jobCardId`), then pass returned `filePath` in the submit body.
4. **Optional link to PO:** Dropdown of client POs (e.g. from `GET api/purchaseorders?status=...` filtered by job’s site/client), or optional `purchaseOrderId` field.
5. Call **POST** `api/jobcardwork/{jobCardId}/documents` with body:
   - `documentType: "ClientPO"`
   - `filePath: "<from upload>"`
   - `notes: optional`
   - `purchaseOrderId: "<selected PO id or null>"`

---

## Backend file checklist

- `Services/IEmailService.cs` – attachPdf parameter added.
- `Services/EmailService.cs` – IDocumentService, attachment in SendAsync, quote/invoice attach PDF.
- `Services/PaymentReminderHostedService.cs` – new file.
- `Program.cs` – `AddHostedService<PaymentReminderHostedService>()`.
- `Controllers/QuotesController.cs` – SendEmail attachPdf query.
- `Controllers/InvoicesController.cs` – SendEmail attachPdf query.
- `DTOs/JobCards/JobCardWorkDto.cs` – SubmitDocumentRequest.PurchaseOrderId, JobCardDocumentDto.PurchaseOrderId/Number.
- `Controllers/JobCardWorkController.cs` – SubmitDocument PurchaseOrderId, GetJob Include PurchaseOrder on documents.
- `Models/Part.cs` – new file.
- `Data/ApplicationDbContext.cs` – DbSet<Part>, Part configuration.
- `Data/Migrations/20260215000000_AddPartsInventory.cs` – new migration.
- `DTOs/Parts/PartDto.cs` – new file.
- `Controllers/PartsController.cs` – new file.

## Frontend file checklist

- `core/services/parts.service.ts` – new file.
- Still to add: Suppliers nav link, Suppliers list/detail/form, Parts list/detail/form, Job documents “Upload client PO” with optional PO link.
