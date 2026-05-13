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
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
}

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
  lineItems: EditableLineItem[] = [];
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
      this.invoiceBlockedByUnacceptedQuote = false;
      this.loadJobAndQuote();
    }
  }

  updateAmountFromLines(): void {
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
          if (this.lineItems.length) this.amount = this.totalFromLines;
        }
      }
    });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.parts.filter(p => !!p.isLabour === labour);
  }

  onPartSelect(idx: number, partId: string | null): void {
    const row = this.lineItems[idx];
    if (!row) return;
    row.partId = partId ?? undefined;
    if (partId) {
      const part = this.parts.find(p => p.id === partId);
      if (part) {
        row.description = part.name;
        if (part.unitPrice != null) row.unitPrice = part.unitPrice;
      }
    } else {
      row.description = row.lineType === 'Labour' ? 'Labour' : '';
    }
    this.updateAmountFromLines();
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
    if (this.amount == null || this.amount < 0) {
      this.error = 'Amount is required.';
      return;
    }
    if (!this.dueDate) {
      this.error = 'Due date is required.';
      return;
    }
    const lineItemsInput: InvoiceLineItemInput[] | undefined = this.lineItems.length > 0
      ? this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0).map(li => ({
          lineType: li.lineType,
          description: li.description,
          quantity: li.quantity,
          unitPrice: li.unitPrice,
          discountPercent: li.discountPercent ?? 0,
          partId: li.partId
        }))
      : undefined;
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
