import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { QuotesService, CreateQuoteRequest } from '../../../core/services/quotes.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { ToastService } from '../../../core/services/toast.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';

type QuoteInputMode = 'materials' | 'guestimation' | 'upload';

interface QuoteLineRow {
  lineType: string;
  description: string;
  unit?: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
}

@Component({
  selector: 'app-quote-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './quote-add.component.html',
  styleUrl: './quote-add.component.scss'
})
export class QuoteAddComponent implements OnInit {
  clientId: string | null = null;
  siteId: string | null = null;
  serviceRequestId: string | null = null;
  jobCardId: string | null = null;
  returnTo: string | null = null;
  description = '';
  notes = '';
  validUntil = '';
  quoteInputMode: QuoteInputMode = 'guestimation';
  discountMode: 'None' | 'Global' | 'PerItem' = 'None';
  globalDiscountPercent = 0;
  quoteAmount: number | null = null;
  deferPricing = false;
  uploadedQuoteFile: File | null = null;
  lineItems: QuoteLineRow[] = [];
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;
  quoteParts: PartDto[] = [];
  clients: ClientDto[] = [];
  sites: SiteDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private quotesService = inject(QuotesService);
  private sitesService = inject(SitesService);
  private clientsService = inject(ClientsService);
  private partsService = inject(PartsService);
  private toast = inject(ToastService);

  get fromServiceRequest(): boolean {
    return !!this.serviceRequestId;
  }

  goBack(): void {
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/quotes']);
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['serviceRequestId']) this.serviceRequestId = q['serviceRequestId'];
    if (q['siteId']) this.siteId = q['siteId'];
    if (q['clientId']) this.clientId = q['clientId'];
    if (q['jobCardId']) this.jobCardId = q['jobCardId'];
    if (q['returnTo']) this.returnTo = sanitizeInternalReturnTo(q['returnTo']);
    this.loading = true;
    this.clientsService.list().subscribe({
      next: (list) => {
        this.clients = list;
        if (this.clientId) {
          this.sitesService.list(this.clientId, true).subscribe({
            next: (sites) => {
              this.sites = sites;
              if (this.siteId && !sites.some(s => s.id === this.siteId)) {
                this.siteId = null;
              }
              this.loading = false;
              this.loadQuoteParts();
            },
            error: () => (this.loading = false)
          });
        } else {
          this.sites = [];
          this.siteId = null;
          this.loading = false;
          this.loadQuoteParts();
        }
      },
      error: () => (this.loading = false)
    });
  }

  onClientChange(): void {
    if (this.fromServiceRequest) return;
    this.siteId = null;
    this.sites = [];
    if (!this.clientId) return;
    this.sitesService.list(this.clientId, true).subscribe({
      next: (sites) => (this.sites = sites)
    });
  }

  loadQuoteParts(): void {
    this.partsService.list().subscribe({ next: (p) => (this.quoteParts = p) });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.quoteParts.filter(p => !!p.isLabour === labour);
  }

  get quoteTotalFromLines(): number {
    const valid = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const subtotal = valid.reduce((s, li) => s + li.quantity * li.unitPrice, 0);
    const perItemDiscount = this.discountMode === 'PerItem'
      ? valid.reduce((s, li) => s + (li.quantity * li.unitPrice * this.clampPercent(li.discountPercent) / 100), 0)
      : 0;
    const afterPerItem = subtotal - perItemDiscount;
    const globalDiscount = this.discountMode === 'Global'
      ? afterPerItem * this.clampPercent(this.globalDiscountPercent) / 100
      : 0;
    return Math.max(0, afterPerItem - globalDiscount);
  }

  get quoteSubtotalFromLines(): number {
    return this.lineItems
      .filter(li => li.quantity > 0 && li.unitPrice >= 0)
      .reduce((s, li) => s + li.quantity * li.unitPrice, 0);
  }

  get quoteDiscountFromLines(): number {
    return Math.max(0, this.quoteSubtotalFromLines - this.quoteTotalFromLines);
  }

  get showPerItemDiscountColumn(): boolean {
    return this.discountMode === 'PerItem';
  }

  lineSubtotal(li: QuoteLineRow): number {
    return Math.max(0, li.quantity * li.unitPrice);
  }

  lineDiscountAmount(li: QuoteLineRow): number {
    if (this.discountMode !== 'PerItem') return 0;
    return this.lineSubtotal(li) * this.clampPercent(li.discountPercent) / 100;
  }

  lineDiscountedTotal(li: QuoteLineRow): number {
    return Math.max(0, this.lineSubtotal(li) - this.lineDiscountAmount(li));
  }

  get effectiveGuestimationAmount(): number {
    const base = this.deferPricing ? 0 : (this.quoteAmount ?? 0);
    if (this.discountMode !== 'Global') return Math.max(0, base);
    return Math.max(0, base - (base * this.clampPercent(this.globalDiscountPercent) / 100));
  }

  private clampPercent(value: number | null | undefined): number {
    const n = Number(value ?? 0);
    if (!Number.isFinite(n)) return 0;
    return Math.min(100, Math.max(0, n));
  }

  onQuoteInputModeChange(mode: QuoteInputMode): void {
    this.quoteInputMode = mode;
    if (mode === 'materials') {
      this.quoteAmount = null;
      this.deferPricing = false;
      this.uploadedQuoteFile = null;
    } else if (mode === 'upload') {
      this.lineItems = [];
      this.quoteAmount = null;
      this.deferPricing = false;
      this.discountMode = 'None';
      this.globalDiscountPercent = 0;
    } else {
      this.lineItems = [];
      this.uploadedQuoteFile = null;
      if (this.discountMode === 'PerItem') this.discountMode = 'None';
    }
  }

  onDiscountModeChange(mode: 'None' | 'Global' | 'PerItem'): void {
    this.discountMode = mode;
    if (mode !== 'Global') this.globalDiscountPercent = 0;
    if (mode !== 'PerItem') {
      this.lineItems = this.lineItems.map(li => ({ ...li, discountPercent: 0 }));
    }
    if (this.quoteInputMode !== 'materials' && mode === 'PerItem') {
      this.discountMode = 'None';
    }
  }

  onUploadedQuoteSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadedQuoteFile = input.files?.[0] ?? null;
  }

  addLineItem(lineType: 'Labour' | 'Part'): void {
    this.lineItems.push({
      lineType,
      description: '',
      unit: '',
      quantity: 1,
      unitPrice: 1,
      discountPercent: 0
    });
    this.lineItemsPage = Math.max(1, Math.ceil(this.lineItems.length / this.lineItemsPageSize));
  }

  removeLineItem(idx: number): void {
    this.lineItems.splice(idx, 1);
    const maxPage = Math.max(1, Math.ceil(this.lineItems.length / this.lineItemsPageSize));
    this.lineItemsPage = Math.min(this.lineItemsPage, maxPage);
  }

  onPartSelect(idx: number, partId: string | null): void {
    const row = this.lineItems[idx];
    if (!row) return;
    row.partId = partId ?? undefined;
    if (partId) {
      const part = this.quoteParts.find(p => p.id === partId);
      if (part) {
        row.description = part.description?.trim() || '';
        row.unit = part.unit?.trim() || '';
        if (part.unitPrice != null) row.unitPrice = part.unitPrice;
      }
    } else {
      row.description = '';
      row.unit = '';
    }
  }

  canSave(): boolean {
    if (!this.clientId || !this.siteId) return false;
    if (this.quoteInputMode === 'upload') return !!this.uploadedQuoteFile;
    if (!this.description.trim()) return false;
    if (this.quoteInputMode === 'materials') {
      const valid = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
      if (valid.length === 0) return false;
      const partRowsMissingSelection = valid.some(li => li.lineType === 'Part' && !li.partId);
      const rowsMissingDescription = valid.some(li => !li.description?.trim());
      return !partRowsMissingSelection && !rowsMissingDescription;
    }
    return this.deferPricing || (this.quoteAmount != null && this.quoteAmount >= 0);
  }

  save(): void {
    this.error = null;
    if (!this.clientId || !this.siteId) {
      this.error = 'Client and site are required.';
      this.toast.error('Please select client and site.');
      return;
    }
    if (this.quoteInputMode === 'upload') {
      if (!this.uploadedQuoteFile) {
        this.error = 'Select a quote file to upload.';
        this.toast.error(this.error);
        return;
      }
      this.submitting = true;
      this.quotesService.upload({
        clientId: this.clientId,
        siteId: this.siteId,
        serviceRequestId: this.serviceRequestId || undefined,
        jobCardId: this.jobCardId || undefined,
        file: this.uploadedQuoteFile
      }).subscribe({
        next: (quote) => {
          this.submitting = false;
          this.toast.success(this.jobCardId ? 'Uploaded quote saved and linked to job card.' : 'Uploaded quote saved.');
          const back = sanitizeInternalReturnTo(this.returnTo);
          if (back) this.router.navigateByUrl(back);
          else this.router.navigate(['/quotes', quote.id]);
        },
        error: (err) => {
          this.submitting = false;
          const msg = err.error?.message || 'Failed to upload quote.';
          this.error = msg;
          this.toast.error(msg);
        }
      });
      return;
    }
    const validLines = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const hasValidLines = this.quoteInputMode === 'materials' && validLines.length > 0;
    if (this.quoteInputMode === 'materials' && !hasValidLines) {
      this.error = 'Add at least one line item with quantity and price when building with materials.';
      this.toast.error(this.error);
      return;
    }
    if (this.quoteInputMode === 'materials' && validLines.some(li => li.lineType === 'Part' && !li.partId)) {
      this.error = 'Each Part line must select a part item so it can attach to the job.';
      this.toast.error(this.error);
      return;
    }
    if (this.quoteInputMode === 'materials' && validLines.some(li => !li.description?.trim())) {
      this.error = 'Each quote line must include a description.';
      this.toast.error(this.error);
      return;
    }
    if (this.quoteInputMode === 'guestimation' && !this.deferPricing && (this.quoteAmount == null || this.quoteAmount < 0)) {
      this.error = 'Enter amount or check "Sort price later".';
      this.toast.error(this.error);
      return;
    }
    if (!this.description.trim()) {
      this.error = 'Description is required.';
      this.toast.error('Description is required.');
      return;
    }
    const amt = hasValidLines
      ? this.quoteTotalFromLines
      : this.effectiveGuestimationAmount;
    const body: CreateQuoteRequest = {
      clientId: this.clientId,
      siteId: this.siteId,
      serviceRequestId: this.serviceRequestId || undefined,
      jobCardId: this.jobCardId || undefined,
      amount: amt,
      deferPricing: this.deferPricing,
      discountMode: this.discountMode,
      globalDiscountPercent: this.discountMode === 'Global' ? this.clampPercent(this.globalDiscountPercent) : 0,
      currency: 'ZAR',
      description: this.description.trim(),
      notes: this.notes.trim() || undefined,
      validUntil: this.validUntil || undefined,
      lineItems: hasValidLines ? validLines.map(li => ({
        lineType: li.lineType,
        description: li.description,
        quantity: li.quantity,
        unitPrice: li.unitPrice,
        discountPercent: this.discountMode === 'PerItem' ? this.clampPercent(li.discountPercent) : 0,
        partId: li.partId
      })) : undefined
    };
    this.submitting = true;
    this.quotesService.create(body).subscribe({
      next: (quote) => {
        this.submitting = false;
        this.toast.success(this.jobCardId ? 'Quote saved and linked to job card.' : 'Quote saved.');
        const back = sanitizeInternalReturnTo(this.returnTo);
        if (back) {
          this.router.navigateByUrl(back);
        } else {
          this.router.navigate(['/quotes', quote.id]);
        }
      },
      error: (err) => {
        this.submitting = false;
        const msg = err.error?.message || 'Failed to create quote.';
        this.error = msg;
        this.toast.error(msg);
      }
    });
  }
}
