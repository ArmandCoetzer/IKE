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
type QuoteDiscountMode = 'None' | 'Global' | 'PerItem' | 'PerItemAndGlobal';

interface QuoteLineRow {
  lineType: string;
  code?: string;
  description: string;
  unit?: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
  matchStatus?: string;
  addMissingItemToSystem?: boolean;
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
  discountMode: QuoteDiscountMode = 'None';
  globalDiscountPercent = 0;
  quoteAmount: number | null = null;
  deferPricing = false;
  uploadedQuoteFile: File | null = null;
  uploadPreviewLoading = false;
  uploadPreviewReady = false;
  uploadPreviewDialogOpen = false;
  uploadPreviewApproved = false;
  extractedQuoteNumber: string | null = null;
  extractedSupplierName: string | null = null;
  extractedSourceCompanyName: string | null = null;
  extractedClientName: string | null = null;
  selectedClientNameFromPreview: string | null = null;
  clientNameMatchesSelected = true;
  clientMismatchApproved = false;
  extractedText: string | null = null;
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
    if (this.quoteInputMode === 'upload') {
      this.clearUploadPreview();
    }
    if (!this.clientId) return;
    this.sitesService.list(this.clientId, true).subscribe({
      next: (sites) => (this.sites = sites)
    });
  }

  onSiteChange(): void {
    if (this.quoteInputMode === 'upload') {
      this.clearUploadPreview();
    }
  }

  loadQuoteParts(): void {
    this.partsService.list().subscribe({ next: (p) => (this.quoteParts = p) });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.quoteParts.filter(p => !!p.isLabour === labour);
  }

  stockDisplayLabel(part: PartDto): string {
    if (part.isLabour) return part.name;
    const available = part.availableQuantity ?? part.quantity ?? 0;
    const reserved = part.reservedForActiveJobsQuantity ?? 0;
    const suffix = reserved > 0 ? ` (${reserved} taken for active jobs)` : '';
    return `${part.name} (${available} in stock${suffix})`;
  }

  get quoteTotalFromLines(): number {
    const valid = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const subtotal = valid.reduce((s, li) => s + li.quantity * li.unitPrice, 0);
    const perItemDiscount = (this.discountMode === 'PerItem' || this.quoteInputMode === 'upload')
      ? valid.reduce((s, li) => s + (li.quantity * li.unitPrice * this.clampPercent(li.discountPercent) / 100), 0)
      : 0;
    const afterPerItem = subtotal - perItemDiscount;
    const globalDiscount = (this.discountMode === 'Global' || this.discountMode === 'PerItemAndGlobal' || (this.quoteInputMode === 'upload' && this.clampPercent(this.globalDiscountPercent) > 0))
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
    if (this.discountMode !== 'PerItem' && this.quoteInputMode !== 'upload') return 0;
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
      this.clearUploadPreview();
    } else if (mode === 'upload') {
      this.lineItems = [];
      this.quoteAmount = null;
      this.deferPricing = false;
      this.discountMode = 'None';
      this.globalDiscountPercent = 0;
      this.clearUploadPreview(false);
    } else {
      this.lineItems = [];
      this.uploadedQuoteFile = null;
      this.clearUploadPreview();
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

  onUploadOverallDiscountChange(): void {
    if (this.quoteInputMode !== 'upload') return;
    const hasLineDiscounts = this.lineItems.some(li => this.clampPercent(li.discountPercent) > 0);
    const hasGlobalDiscount = this.clampPercent(this.globalDiscountPercent) > 0;
    this.discountMode = hasLineDiscounts && hasGlobalDiscount
      ? 'PerItemAndGlobal'
      : hasLineDiscounts
        ? 'PerItem'
        : hasGlobalDiscount
          ? 'Global'
          : 'None';
    this.uploadPreviewApproved = false;
  }

  onUploadedQuoteSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!this.clientId || !this.siteId) {
      input.value = '';
      this.uploadedQuoteFile = null;
      this.toast.error('Select client and site before uploading a quote.');
      return;
    }
    this.uploadedQuoteFile = input.files?.[0] ?? null;
    this.clearUploadPreview(false);
    if (this.uploadedQuoteFile) {
      this.previewUploadedQuote();
    }
  }

  clearUploadPreview(clearFile = true): void {
    if (clearFile) this.uploadedQuoteFile = null;
    this.uploadPreviewLoading = false;
    this.uploadPreviewReady = false;
    this.uploadPreviewDialogOpen = false;
    this.uploadPreviewApproved = false;
    this.extractedQuoteNumber = null;
    this.extractedSupplierName = null;
    this.extractedSourceCompanyName = null;
    this.extractedClientName = null;
    this.selectedClientNameFromPreview = null;
    this.clientNameMatchesSelected = true;
    this.clientMismatchApproved = false;
    this.extractedText = null;
    if (this.quoteInputMode === 'upload') {
      this.globalDiscountPercent = 0;
      this.discountMode = 'None';
    }
    if (this.quoteInputMode === 'upload') {
      this.lineItems = [];
    }
  }

  previewUploadedQuote(): void {
    this.error = null;
    if (!this.uploadedQuoteFile) return;
    if (!this.clientId || !this.siteId) {
      this.toast.error('Select client and site before uploading a quote.');
      return;
    }
    this.uploadPreviewLoading = true;
    this.quotesService.uploadPreview({
      clientId: this.clientId,
      siteId: this.siteId,
      file: this.uploadedQuoteFile
    }).subscribe({
      next: (preview) => {
        this.uploadPreviewLoading = false;
        this.uploadPreviewReady = true;
        this.uploadPreviewApproved = false;
        this.uploadPreviewDialogOpen = true;
        this.extractedQuoteNumber = preview.extractedQuoteNumber ?? null;
        this.extractedSupplierName = preview.extractedSupplierName ?? null;
        this.extractedSourceCompanyName = preview.extractedSourceCompanyName ?? preview.extractedSupplierName ?? null;
        this.extractedClientName = preview.extractedClientName ?? null;
        this.selectedClientNameFromPreview = preview.selectedClientName ?? null;
        this.clientNameMatchesSelected = preview.clientNameMatchesSelected;
        this.clientMismatchApproved = false;
        this.extractedText = preview.extractedText ?? null;
        this.description = preview.description || this.description;
        this.quoteAmount = preview.extractedAmount ?? null;
        this.globalDiscountPercent = this.clampPercent(preview.overallDiscountPercent ?? 0);
        this.validUntil = preview.validUntil ? preview.validUntil.substring(0, 10) : '';
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
        this.onUploadOverallDiscountChange();
        this.toast.success(this.lineItems.length ? 'Quote details extracted for review.' : 'Quote uploaded for review. No line items were detected.');
      },
      error: (err) => {
        this.uploadPreviewLoading = false;
        const msg = err.error?.message || 'Failed to extract quote details.';
        this.error = msg;
        this.toast.error(msg);
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
    if (!this.description.trim()) {
      this.toast.error('Description is required before approving the quote.');
      return;
    }
    if (!this.clientNameMatchesSelected && !this.clientMismatchApproved) {
      this.toast.error('Confirm that the selected client is correct before approving this quote.');
      return;
    }
    const missingItemError = this.validateMissingQuoteItemsForApproval();
    if (missingItemError) {
      this.toast.error(missingItemError);
      return;
    }
    const invalidLine = this.lineItems.some(li => li.quantity <= 0 || li.unitPrice < 0 || !li.description.trim());
    if (invalidLine) {
      this.toast.error('Check extracted lines: each saved line needs a description, quantity and valid price.');
      return;
    }
    this.uploadPreviewApproved = true;
    this.uploadPreviewDialogOpen = false;
    this.toast.success('Quote details approved.');
  }

  private validateMissingQuoteItemsForApproval(): string | null {
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

      const description = this.normalizeMissingItemKey(li.description);
      if (seenDescriptions.has(description)) return `Duplicate item description "${li.description.trim()}" cannot be added more than once.`;
      seenDescriptions.add(description);
    }
    return null;
  }

  get canSelectMissingLineItems(): boolean {
    return this.lineItems.some(li => !li.partId);
  }

  get allMissingLineItemsSelected(): boolean {
    const rows = this.lineItems.filter(li => !li.partId);
    return rows.length > 0 && rows.every(li => !!li.addMissingItemToSystem);
  }

  get someMissingLineItemsSelected(): boolean {
    return this.lineItems.some(li => !li.partId && !!li.addMissingItemToSystem);
  }

  toggleAllMissingLineItems(checked: boolean): void {
    this.lineItems = this.lineItems.map(li => li.partId ? li : { ...li, addMissingItemToSystem: checked });
    this.uploadPreviewApproved = false;
  }

  private normalizeMissingItemKey(value: string): string {
    return value.trim().replace(/\s+/g, ' ').toLowerCase();
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
        if (this.quoteInputMode !== 'upload' || !row.description.trim()) {
          row.description = part.description?.trim() || part.name;
        }
        row.unit = part.unit?.trim() || '';
        if (part.unitPrice != null) row.unitPrice = part.unitPrice;
        row.matchStatus = 'Mapped';
        row.addMissingItemToSystem = false;
      }
    } else if (this.quoteInputMode !== 'upload') {
      row.description = '';
      row.unit = '';
    } else {
      row.matchStatus = 'Manual';
    }
  }

  canSave(): boolean {
    if (!this.clientId || !this.siteId) return false;
    if (this.quoteInputMode === 'upload') {
      return !!this.uploadedQuoteFile && !this.uploadPreviewLoading && this.uploadPreviewReady && this.uploadPreviewApproved && !!this.description.trim();
    }
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
      if (!this.uploadPreviewApproved) {
        this.error = 'Review and approve the extracted quote details before saving.';
        this.toast.error(this.error);
        return;
      }
      if (!this.description.trim()) {
        this.error = 'Description is required.';
        this.toast.error(this.error);
        return;
      }
      const validUploadLines = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0 && li.description.trim());
      const uploadAmount = this.quoteAmount ?? (validUploadLines.length > 0 ? this.quoteTotalFromLines : 0);
      this.submitting = true;
      this.quotesService.upload({
        clientId: this.clientId,
        siteId: this.siteId,
        serviceRequestId: this.serviceRequestId || undefined,
        jobCardId: this.jobCardId || undefined,
        amount: uploadAmount,
        globalDiscountPercent: this.clampPercent(this.globalDiscountPercent),
        description: this.description.trim(),
        notes: this.notes.trim() || undefined,
        validUntil: this.validUntil || undefined,
        lineItems: validUploadLines.map(li => ({
          lineType: li.lineType,
          description: li.description.trim(),
          quantity: li.quantity,
          unitPrice: li.unitPrice,
          discountPercent: this.clampPercent(li.discountPercent),
          partId: li.partId,
          code: li.code?.trim() || undefined,
          addMissingItemToSystem: !!li.addMissingItemToSystem
        })),
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
