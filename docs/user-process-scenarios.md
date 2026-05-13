# Tradion: User process flows (Quote → PO → Job card → Invoice → Payment)

This document describes three ways to run a process from start to **invoice and payment**, including when to add a client and how the client’s PO is handled.

---

## Scenario 1: You send a quote first — client sends the PO

**When to use:** You quote first; the client accepts and sends you their purchase order (PO). You record their PO in the system.

### 1. Add the client (and site) first

1. Go to **Resources** → **Clients** → **Add client**.
2. Enter **Company name** (required), and optionally Contact name, Phone, Email. Save.
3. Go to **Resources** → **Sites** → **Add site**.
4. Create at least one **Site** for that client (name, address, etc.) and link it to the client. Save.

### 2. Create and send the quote

5. Go to **Finance** → **Quotes** → **Add quote**.
6. Select the **Client** and **Site**, enter **Amount**, **Description**, and optionally Valid until and Notes. Save.
7. Open the quote (Quote detail) and click **Send to client** so the client receives the quote.
8. *(Outside the system: client reviews and accepts, then sends you their PO — document and/or PO number.)*

### 3. Record the client’s PO and create your PO

9. Open the same quote again. Click **Create PO** (this creates a purchase order in Tradion linked to the quote, with client, site, and amount pre-filled).
10. You are taken to **Add purchase order**. Confirm Client, Site, Amount (and optional Client PO number). Save.
11. Go to **Finance** → **Purchase orders**, open that PO, then click **Edit**.
12. Enter the **Client PO number** (if you have it) and upload the **Client PO file** (PDF/image) if the client sent a document. Save.

### 4. Create job card and do the work

13. From the **PO detail** page, click **Create job card** (site, quote, and PO are linked).
14. Complete the job card (job type, documents, parts, permits as needed). Use **Job cards** to track status (Open → In Progress → Completed).

### 5. Invoice and payment

15. Open the **Job card** and click **Create invoice** (or use **Finance** → **Invoices** → **Add invoice** and select the job card).
16. Fill in **Amount**, **Due date**, and optional Notes. Save.
17. From the **Invoice detail** page, click **Send to client** so the client receives the invoice.
18. When the client pays, open the invoice again and click **Mark paid**.

**End:** Process is complete from quote to invoice and payment; the client’s PO was recorded on the PO.

---

## Scenario 2: Client sends a request first — then you quote; client sends the PO

**When to use:** The client requests work first (e.g. by email/phone). You add their request in Tradion, then quote; they accept and send a PO. You record their PO.

### 1. Add the client (and site) first

1. Go to **Resources** → **Clients** → **Add client**.
2. Enter **Company name** (required) and optional contact details. Save.
3. Go to **Resources** → **Sites** → **Add site**.
4. Create at least one **Site** for that client and link it to the client. Save.

### 2. Record the client’s request

5. Go to **Work** → **Requests** (Service requests) → **Add service request**.
6. Select the **Site**, optionally **Equipment**, enter **Description** (what the client asked for), and optionally Priority and Due date. Save.
7. *(This represents “we received a request from the client.”)*

### 3. Create and send a quote from the request

8. Open the **Service request** (request detail). Click **Create quote** (quote will be linked to this request and site).
9. On **Add quote**, select **Client** and **Site**, enter **Amount** and **Description**. Save.
10. Open the quote and click **Send to client**.
11. *(Outside the system: client accepts and sends you their PO.)*

### 4. Record the client’s PO and create your PO

12. Open the same **Quote** and click **Create PO** (creates a PO linked to the quote).
13. On **Add purchase order**, confirm Client, Site, Amount; optionally enter Client PO number. Save.
14. Open that **PO** → **Edit**. Enter **Client PO number** and upload **Client PO file** if the client sent a document. Save.

### 5. Create job card and do the work

15. From the **PO detail** page, click **Create job card**.
16. Complete and track the job (status, documents, parts, permits as needed).

### 6. Invoice and payment

17. Open the **Job card** → **Create invoice**. Fill Amount, Due date, Notes as needed. Save.
18. On the **Invoice** detail, click **Send to client**.
19. When paid, open the invoice and click **Mark paid**.

**End:** Process is complete from client request → quote → client PO → job card → invoice and payment.

---

## Scenario 3: You send a quote first — you create the PO (client does not send a PO)

**When to use:** You quote first; the client accepts but does **not** send a PO. You create the PO yourself in Tradion.

### 1. Add the client (and site) first

1. Go to **Resources** → **Clients** → **Add client**.
2. Enter **Company name** (required) and optional contact details. Save.
3. Go to **Resources** → **Sites** → **Add site**.
4. Create at least one **Site** for that client and link it to the client. Save.

### 2. Create and send the quote

5. Go to **Finance** → **Quotes** → **Add quote**.
6. Select **Client** and **Site**, enter **Amount**, **Description**, and optional fields. Save.
7. Open the quote and click **Send to client**.
8. *(Outside the system: client accepts; no PO document is sent by the client.)*

### 3. Create the PO yourself (no client PO to record)

9. Open the same **Quote** and click **Create PO** (this creates the purchase order in the system; no client PO number or file needed).
10. On **Add purchase order**, confirm Client, Site, and Amount. Leave **Client PO number** blank. Save.
11. You do **not** need to edit the PO to add a client PO file or number.

### 4. Create job card and do the work

12. From the **PO detail** page, click **Create job card**.
13. Complete and track the job (status, documents, parts, permits as needed).

### 5. Invoice and payment

14. Open the **Job card** → **Create invoice**. Fill Amount, Due date, Notes as needed. Save.
15. On the **Invoice** detail, click **Send to client**.
16. When the client pays, open the invoice and click **Mark paid**.

**End:** Process is complete from quote to invoice and payment; the PO was created by you, not sent by the client.

---

## Summary

| Scenario | Start              | Client sends PO? | Your actions for PO                          |
|----------|--------------------|------------------|----------------------------------------------|
| 1        | You send quote     | Yes              | Create PO from quote → Edit PO: add client PO # and file |
| 2        | Client request     | Yes              | Create quote from request → send quote → Create PO from quote → Edit PO: add client PO # and file |
| 3        | You send quote     | No               | Create PO from quote only (no client PO # or file) |

In all three flows, the path after the PO is the same: **Create job card from PO** → do the work → **Create invoice from job card** → **Send to client** → **Mark paid** when payment is received.
