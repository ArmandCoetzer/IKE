import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InvoicesService, CreateInvoiceRequest, InvoiceLineItemInput } from '../../../core/services/invoices.service';
import { JobCardsService, JobCardListDto } from '../../../core/services/job-cards.service';
import { JobCardWorkService } from '../../../core/services/job-card-work.service';
import { QuotesService } from '../../../core/services/quotes.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { ServiceRequestsService } from '../../../core/services/service-requests.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';

export interface EditableLineItem {
  lineType: string;
  code?: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
  matchStatus?: string;
  addMissingItemToSystem?: boolean;
}

type InvoiceInputMode = 'manual' | 'upload';

@Component({
  selector: 'app-invoice-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './invoice-add.component.html',
  styleUrl: './invoice-add.component.scss'
})
export class InvoiceAddComponent implements OnInit {
  jobCardId: string | null = null;
  quoteId: string | null = null;
  clientId: string | null = null;
  siteId: string | null = null;
  returnTo: string | null = null;
  amount: number | null = null;
  dueDate = '';
  notes = '';
  uploadedInvoiceFile: File | null = null;
  invoiceInputMode: InvoiceInputMode = 'manual';
  uploadPreviewLoading = false;
  uploadPreviewReady = false;
  uploadPreviewDialogOpen = false;
  uploadPreviewApproved = false;
  extractedInvoiceNumber: string | null = null;
  extractedInvoiceAmount: number | null = null;
  extractedSourceCompanyName: string | null = null;
  extractedClientName: string | null = null;
  selectedClientNameFromPreview: string | null = null;
  clientNameMatchesSelected = true;
  clientMismatchApproved = false;
  lineItems: EditableLineItem[] = [];
  expectedJobLineItems: EditableLineItem[] = [];
  invoiceComparisonNotes: string[] = [];
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;
  jobCards: JobCardListDto[] = [];
  sites: SiteDto[] = [];
  clients: ClientDto[] = [];
  parts: PartDto[] = [];
  loading = false;
  submitting = false;
  sendingAfterCreate = false;
  showSendConfirmationModal = false;
  pendingCreatedInvoiceId: string | null = null;
  error: string | null = null;
  siteReadOnly = false;
  /** When the job has a quote that is not yet Accepted, creation is blocked (API enforces too). */
  invoiceBlockedByUnacceptedQuote = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private serviceRequestsService: ServiceRequestsService,
    private invoicesService: InvoicesService,
    private jobCardsService: JobCardsService,
    private jobCardWorkService: JobCardWorkService,
    private quotesService: QuotesService,
    private sitesService: SitesService,
    private clientsService: ClientsService,
    private partsService: PartsService
  ) {}

  get subtotalFromLines(): number {
    return this.lineItems.reduce((sum, li) => sum + this.lineSubtotal(li), 0);
  }

  get discountFromLines(): number {
    return this.lineItems.reduce((sum, li) => sum + this.lineDiscountAmount(li), 0);
  }

  get showDiscountColumns(): boolean {
    return this.discountFromLines > 0;
  }

  get totalFromLines(): number {
    return this.lineItems.reduce((sum, li) => sum + this.lineDiscountedTotal(li), 0);
  }

  lineSubtotal(li: EditableLineItem): number {
    return Math.max(0, li.quantity * li.unitPrice);
  }

  lineDiscountAmount(li: EditableLineItem): number {
    const pct = Math.min(100, Math.max(0, Number(li.discountPercent ?? 0)));
    return this.lineSubtotal(li) * pct / 100;
  }

  lineDiscountedTotal(li: EditableLineItem): number {
    return Math.max(0, this.lineSubtotal(li) - this.lineDiscountAmount(li));
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['jobCardId']) {
      this.jobCardId = q['jobCardId'];
      this.siteReadOnly = true;
    }
    if (q['siteId']) this.siteId = q['siteId'];
    if (q['returnTo']) this.returnTo = sanitizeInternalReturnTo(q['returnTo']);
    this.loading = true;
    this.jobCardsService.list({ status: 'Completed' }).subscribe({
      next: (r) => {
        this.jobCards = r.items;
        this.sitesService.list(undefined, true).subscribe({
          next: (sites) => {
            this.sites = sites;
            this.clientsService.list().subscribe({
              next: (clients) => {
                this.clients = clients;
                this.partsService.list().subscribe({ next: (p) => (this.parts = p) });
                if (this.jobCardId) {
                  this.loadJobAndQuote();
                } else {
                  this.loading = false;
                }
              },
              error: () => (this.loading = false)
            });
          },
          error: () => (this.loading = false)
        });
      },
      error: () => (this.loading = false)
    });
  }

  private loadJobAndQuote(): void {
    if (!this.jobCardId) return;
    this.jobCardWorkService.get(this.jobCardId).subscribe({
      next: (job) => {
        this.siteId = job.siteId ?? this.siteId;
        this.clientId = job.companyId ?? this.clientId;
        this.amount = job.quoteAmount ?? this.amount ?? 0;
        const due = new Date();
        due.setDate(due.getDate() + 14);
        if (!this.dueDate) this.dueDate = due.toISOString().slice(0, 10);
        if (job.quoteId) {
          this.quoteId = job.quoteId;
          this.invoiceBlockedByUnacceptedQuote = (job.quoteStatus || '').trim().toLowerCase() !== 'accepted';
          this.quotesService.get(job.quoteId).subscribe({
            next: (quote) => {
              this.invoiceBlockedByUnacceptedQuote = (quote.status || '').trim().toLowerCase() !== 'accepted';
              if (quote.lineItems?.length) {
                this.lineItems = quote.lineItems.map(li => ({
                  lineType: li.lineType || 'Labour',
                  description: li.description,
                  quantity: li.quantity,
                  unitPrice: li.unitPrice,
                  discountPercent: li.discountPercent ?? 0,
                  partId: li.partId
                }));
              } else {
                this.lineItems = [];
              }
              const quotePartIds = new Set((quote.lineItems ?? []).filter(li => li.partId).map(li => li.partId!));
              const linesByPartId = new Map(this.lineItems.filter(li => li.partId).map(li => [li.partId!, li]));
              for (const pp of job.plannedParts ?? []) {
                const existing = linesByPartId.get(pp.partId);
                if (existing) {
                  existing.quantity = Math.max(existing.quantity, pp.quantity);
                } else if (!quotePartIds.has(pp.partId)) {
                  this.lineItems.push({
                    lineType: 'Part',
                    description: pp.partName || 'Part',
                    quantity: pp.quantity,
                    unitPrice: 0,
                    discountPercent: 0,
                    partId: pp.partId
                  });
                }
              }
              if (this.lineItems.length) this.amount = this.totalFromLines;
              this.captureExpectedJobLineItems();
              this.addPenaltyLineIfAny(job.serviceRequestId);
              this.loading = false;
            },
            error: () => { this.loading = false; }
          });
        } else {
          this.invoiceBlockedByUnacceptedQuote = false;
          const plannedParts = job.plannedParts ?? [];
          if (plannedParts.length) {
            this.lineItems = plannedParts.map(pp => ({
              lineType: 'Part',
              description: pp.partName || 'Part',
              quantity: pp.quantity,
              unitPrice: 0,
              discountPercent: 0,
              partId: pp.partId
            }));
            this.amount = this.totalFromLines;
          }
          this.captureExpectedJobLineItems();
          this.addPenaltyLineIfAny(job.serviceRequestId);
          this.loading = false;
        }
      },
      error: () => { this.loading = false; }
    });
  }

  onJobSelect(): void {
    const job = this.jobCards.find(j => j.id === this.jobCardId);
    if (job) {
      this.siteId = job.siteId;
      this.quoteId = null;
      this.lineItems = [];
      this.expectedJobLineItems = [];
      this.invoiceComparisonNotes = [];
      this.clearUploadPreview();
      this.invoiceBlockedByUnacceptedQuote = false;
      this.loadJobAndQuote();
    }
  }

  updateAmountFromLines(): void {
    if (this.invoiceInputMode === 'upload') return;
    if (this.lineItems.length > 0) this.amount = this.totalFromLines;
  }

  removeLineItem(idx: number): void {
    this.lineItems.splice(idx, 1);
    this.updateAmountFromLines();
    const maxPage = Math.max(1, Math.ceil(this.lineItems.length / this.lineItemsPageSize));
    this.lineItemsPage = Math.min(this.lineItemsPage, maxPage);
  }

  addLineItem(): void {
    this.lineItems.push({ lineType: 'Labour', description: '', quantity: 0, unitPrice: 0, discountPercent: 0 });
    this.lineItemsPage = Math.max(1, Math.ceil(this.lineItems.length / this.lineItemsPageSize));
  }

  toggleInvoiceInputMode(): void {
    this.invoiceInputMode = this.invoiceInputMode === 'upload' ? 'manual' : 'upload';
  }

  useInvoiceForm(): void {
    this.invoiceInputMode = 'manual';
  }

  useUploadedInvoiceDocument(): void {
    this.invoiceInputMode = 'upload';
  }

  clearUploadPreview(clearFile = true): void {
    if (clearFile) this.uploadedInvoiceFile = null;
    this.uploadPreviewLoading = false;
    this.uploadPreviewReady = false;
    this.uploadPreviewDialogOpen = false;
    this.uploadPreviewApproved = false;
    this.extractedInvoiceNumber = null;
    this.extractedInvoiceAmount = null;
    this.extractedSourceCompanyName = null;
    this.extractedClientName = null;
    this.selectedClientNameFromPreview = null;
    this.clientNameMatchesSelected = true;
    this.clientMismatchApproved = false;
    this.invoiceComparisonNotes = [];
  }

  private captureExpectedJobLineItems(): void {
    this.expectedJobLineItems = this.lineItems.map(li => ({ ...li }));
  }

  private addPenaltyLineIfAny(serviceRequestId?: string): void {
    if (!serviceRequestId) return;
    this.serviceRequestsService.get(serviceRequestId).subscribe({
      next: (sr) => {
        if (sr.penaltyFee != null && sr.penaltyFee > 0) {
          this.lineItems.push({
            lineType: 'Labour',
            description: sr.penaltyNote?.trim() || 'Priority inflation penalty',
            quantity: 1,
        unitPrice: sr.penaltyFee,
        discountPercent: 0
          });
          this.updateAmountFromLines();
        }
      }
    });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.parts.filter(p => !!p.isLabour === labour);
  }

  stockDisplayLabel(part: PartDto): string {
    if (part.isLabour) return part.name;
    const available = part.availableQuantity ?? part.quantity ?? 0;
    const reserved = part.reservedForActiveJobsQuantity ?? 0;
    const suffix = reserved > 0 ? ` (${reserved} taken for active jobs)` : '';
    return `${part.name} (${available} in stock${suffix})`;
  }

  onPartSelect(idx: number, partId: string | null): void {
    const row = this.lineItems[idx];
    if (!row) return;
    row.partId = partId ?? undefined;
    if (partId) {
      const part = this.parts.find(p => p.id === partId);
      if (part) {
        if (this.invoiceInputMode !== 'upload' || !row.description.trim()) {
          row.description = part.name;
        }
        if (part.unitPrice != null) row.unitPrice = part.unitPrice;
        row.matchStatus = 'Mapped';
        row.addMissingItemToSystem = false;
      }
    } else {
      if (this.invoiceInputMode !== 'upload') {
        row.description = row.lineType === 'Labour' ? 'Labour' : '';
      } else {
        row.matchStatus = 'Manual';
      }
    }
    this.updateAmountFromLines();
  }

  onUploadedInvoiceSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!this.jobCardId || !this.siteId || !this.clientId) {
      input.value = '';
      this.uploadedInvoiceFile = null;
      this.error = 'Select a job card, client and site before uploading an invoice.';
      return;
    }
    this.uploadedInvoiceFile = input.files?.[0] ?? null;
    this.invoiceInputMode = 'upload';
    this.clearUploadPreview(false);
    if (this.uploadedInvoiceFile) {
      this.previewUploadedInvoice();
    }
  }

  previewUploadedInvoice(): void {
    this.error = null;
    if (!this.uploadedInvoiceFile) return;
    if (!this.jobCardId || !this.siteId || !this.clientId) {
      this.error = 'Select a job card, client and site before uploading an invoice.';
      return;
    }
    this.uploadPreviewLoading = true;
    this.invoicesService.uploadPreview({
      jobCardId: this.jobCardId,
      clientId: this.clientId,
      siteId: this.siteId,
      file: this.uploadedInvoiceFile
    }).subscribe({
      next: (preview) => {
        this.uploadPreviewLoading = false;
        this.uploadPreviewReady = true;
        this.uploadPreviewApproved = false;
        this.uploadPreviewDialogOpen = true;
        this.extractedInvoiceNumber = preview.extractedInvoiceNumber ?? null;
        this.extractedInvoiceAmount = preview.extractedAmount ?? null;
        this.extractedSourceCompanyName = preview.extractedSourceCompanyName ?? null;
        this.extractedClientName = preview.extractedClientName ?? null;
        this.selectedClientNameFromPreview = preview.selectedClientName ?? null;
        this.clientNameMatchesSelected = preview.clientNameMatchesSelected;
        this.clientMismatchApproved = false;
        this.amount = preview.extractedAmount ?? this.amount;
        this.dueDate = preview.dueDate ? preview.dueDate.substring(0, 10) : this.dueDate;
        this.lineItems = (preview.lineItems ?? []).map(li => ({
          lineType: li.lineType || 'Part',
          code: li.code || '',
          description: li.description || '',
          quantity: li.quantity || 1,
          unitPrice: li.unitPrice || 0,
          discountPercent: li.discountPercent || 0,
          partId: li.suggestedPartId,
          matchStatus: li.matchStatus,
          addMissingItemToSystem: false
        }));
        this.invoiceComparisonNotes = this.buildInvoiceComparisonNotes(this.lineItems);
      },
      error: (err) => {
        this.uploadPreviewLoading = false;
        this.error = err.error?.message || 'Failed to extract invoice details.';
      }
    });
  }

  openUploadPreviewDialog(): void {
    if (this.uploadPreviewReady) {
      this.uploadPreviewApproved = false;
      this.uploadPreviewDialogOpen = true;
    }
  }

  closeUploadPreviewDialog(): void {
    this.uploadPreviewDialogOpen = false;
  }

  approveUploadPreview(): void {
    if (!this.clientNameMatchesSelected && !this.clientMismatchApproved) {
      this.error = 'Confirm that the selected client is correct before approving this invoice.';
      return;
    }
    const invalidLine = this.lineItems.some(li => li.quantity <= 0 || li.unitPrice < 0 || !li.description.trim());
    if (invalidLine) {
      this.error = 'Check extracted lines: each saved line needs a description, quantity and valid price.';
      return;
    }
    if (this.amount == null || this.amount < 0) {
      this.error = 'Invoice amount is required before approving.';
      return;
    }
    if (!this.dueDate) {
      this.error = 'Due date is required before approving.';
      return;
    }
    this.invoiceComparisonNotes = this.buildInvoiceComparisonNotes(this.lineItems);
    const missingItemError = this.validateMissingInvoiceItemsForApproval();
    if (missingItemError) {
      this.error = missingItemError;
      return;
    }
    this.error = null;
    this.uploadPreviewApproved = true;
    this.uploadPreviewDialogOpen = false;
  }

  private validateMissingInvoiceItemsForApproval(): string | null {
    const addMissingLines = this.lineItems.filter(li => !!li.addMissingItemToSystem && !li.partId);
    for (const li of addMissingLines) {
      if (!li.code?.trim()) return 'Each item selected to be added to the system needs a code.';
      if (!li.description?.trim()) return 'Each item selected to be added to the system needs a description/name.';
    }
    const seenCodes = new Set<string>();
    const seenDescriptions = new Set<string>();
    for (const li of addMissingLines) {
      const code = li.code!.trim().toLowerCase();
      if (seenCodes.has(code)) return `Duplicate item code "${li.code!.trim()}" cannot be added more than once.`;
      seenCodes.add(code);

      const description = this.normalizeLineKey(li.description);
      if (seenDescriptions.has(description)) return `Duplicate item description "${li.description.trim()}" cannot be added more than once.`;
      seenDescriptions.add(description);
    }
    return null;
  }

  private buildInvoiceComparisonNotes(invoiceLines: EditableLineItem[]): string[] {
    const expected = this.groupComparableLines(this.expectedJobLineItems);
    const actual = this.groupComparableLines(invoiceLines);
    const notes: string[] = [];

    expected.forEach((expectedLine, key) => {
      const actualLine = actual.get(key);
      if (!actualLine) {
        notes.push(`Expected "${expectedLine.label}" from quote/planned parts was not found on the uploaded invoice.`);
        return;
      }
      if (Math.abs(expectedLine.quantity - actualLine.quantity) > 0.0001) {
        notes.push(`"${expectedLine.label}" quantity differs: quote/planned ${expectedLine.quantity}, invoice ${actualLine.quantity}.`);
      }
    });

    actual.forEach((actualLine, key) => {
      if (!expected.has(key)) {
        notes.push(`Uploaded invoice includes "${actualLine.label}" that was not on the quote/planned parts.`);
      }
    });

    return notes;
  }

  private groupComparableLines(lines: EditableLineItem[]): Map<string, { label: string; quantity: number }> {
    const grouped = new Map<string, { label: string; quantity: number }>();
    for (const line of lines) {
      if (line.quantity <= 0) continue;
      const key = line.partId ? `part:${line.partId}` : `desc:${this.normalizeLineKey(line.description)}`;
      if (key === 'desc:') continue;
      const existing = grouped.get(key);
      if (existing) {
        existing.quantity += Number(line.quantity) || 0;
      } else {
        grouped.set(key, {
          label: line.description?.trim() || line.code?.trim() || 'Line item',
          quantity: Number(line.quantity) || 0
        });
      }
    }
    return grouped;
  }

  private normalizeLineKey(value: string | undefined): string {
    return (value ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
  }

  private buildUploadedInvoiceNotes(): string | undefined {
    const parts = this.notes.trim() ? [this.notes.trim()] : [];
    if (this.invoiceComparisonNotes.length) {
      parts.push(`Uploaded invoice comparison:\n${this.invoiceComparisonNotes.map(note => `- ${note}`).join('\n')}`);
    }
    return parts.length ? parts.join('\n\n') : undefined;
  }

  save(): void {
    this.error = null;
    if (this.invoiceBlockedByUnacceptedQuote) {
      this.error = 'The client must accept the quote before you can create an invoice for this job.';
      return;
    }
    if (!this.jobCardId || !this.siteId) {
      this.error = 'Job card and site are required.';
      return;
    }
    if (this.invoiceInputMode !== 'upload' && (this.amount == null || this.amount < 0)) {
      this.error = 'Amount is required.';
      return;
    }
    if (this.invoiceInputMode !== 'upload' && !this.dueDate) {
      this.error = 'Due date is required.';
      return;
    }
    const lineItemsInput: InvoiceLineItemInput[] | undefined = this.lineItems.length > 0
      ? this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0).map(li => ({
          lineType: li.lineType,
          code: li.code?.trim() || undefined,
          description: li.description,
          quantity: li.quantity,
          unitPrice: li.unitPrice,
          discountPercent: li.discountPercent ?? 0,
          partId: li.partId,
          addMissingItemToSystem: !!li.addMissingItemToSystem
        }))
      : undefined;
    if (this.invoiceInputMode === 'upload') {
      if (!this.uploadedInvoiceFile) {
        this.error = 'Select an invoice document to upload.';
        return;
      }
      if (!this.uploadPreviewApproved) {
        this.error = 'Review and approve the extracted invoice details before saving.';
        return;
      }
      this.invoiceComparisonNotes = this.buildInvoiceComparisonNotes(this.lineItems);
      this.submitting = true;
      this.invoicesService.upload({
        jobCardId: this.jobCardId,
        quoteId: this.quoteId || undefined,
        clientId: this.clientId || undefined,
        siteId: this.siteId,
        amount: this.extractedInvoiceAmount ?? this.amount ?? (lineItemsInput?.length ? this.totalFromLines : 0),
        dueDate: this.dueDate || undefined,
        notes: this.buildUploadedInvoiceNotes(),
        file: this.uploadedInvoiceFile,
        lineItems: lineItemsInput
      }).subscribe({
        next: (invoice) => {
          this.submitting = false;
          this.router.navigate(['/invoices', invoice.id]);
        },
        error: (err) => {
          this.submitting = false;
          this.error = err.error?.message || 'Failed to upload invoice.';
        }
      });
      return;
    }
    const amt = lineItemsInput?.length ? this.totalFromLines : (this.amount ?? 0);
    const body: CreateInvoiceRequest = {
      jobCardId: this.jobCardId,
      quoteId: this.quoteId || undefined,
      clientId: this.clientId || undefined,
      siteId: this.siteId,
      amount: amt,
      dueDate: this.dueDate,
      currency: 'ZAR',
      notes: this.notes.trim() || undefined,
      lineItems: lineItemsInput
    };
    this.submitting = true;
    this.invoicesService.create(body).subscribe({
      next: (inv) => {
        this.submitting = false;
        this.pendingCreatedInvoiceId = inv.id;
        this.showSendConfirmationModal = true;
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to create invoice.';
      }
    });
  }

  cancelSendAfterCreate(): void {
    const id = this.pendingCreatedInvoiceId;
    this.showSendConfirmationModal = false;
    this.pendingCreatedInvoiceId = null;
    if (!id) return;
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/invoices', id]);
  }

  confirmSendAfterCreate(): void {
    const id = this.pendingCreatedInvoiceId;
    if (!id) return;
    this.error = null;
    this.sendingAfterCreate = true;
    this.invoicesService.confirmParts(id).subscribe({
      next: () => {
        this.invoicesService.send(id, undefined, true).subscribe({
          next: () => {
            this.sendingAfterCreate = false;
            this.showSendConfirmationModal = false;
            this.pendingCreatedInvoiceId = null;
            const back = sanitizeInternalReturnTo(this.returnTo);
            if (back) this.router.navigateByUrl(back);
            else this.router.navigate(['/invoices', id]);
          },
          error: (err) => {
            this.sendingAfterCreate = false;
            this.showSendConfirmationModal = false;
            this.error = err?.error?.message || 'Invoice created, but sending failed.';
            this.pendingCreatedInvoiceId = null;
            const back = sanitizeInternalReturnTo(this.returnTo);
            if (back) this.router.navigateByUrl(back);
            else this.router.navigate(['/invoices', id]);
          }
        });
      },
      error: (err) => {
        this.sendingAfterCreate = false;
        this.showSendConfirmationModal = false;
        this.error = err?.error?.message || 'Invoice created, but parts confirmation failed.';
        this.pendingCreatedInvoiceId = null;
        const back = sanitizeInternalReturnTo(this.returnTo);
        if (back) this.router.navigateByUrl(back);
        else this.router.navigate(['/invoices', id]);
      }
    });
  }
}
