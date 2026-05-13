import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { BreadcrumbComponent } from '../../shared/breadcrumb/breadcrumb.component';
import { ClientsService, ClientDto } from '../../core/services/clients.service';
import { SitesService, SiteDto } from '../../core/services/sites.service';
import { QuotesService, CreateQuoteRequest, QuoteDto, QuoteLineItemInput } from '../../core/services/quotes.service';
import { ServiceRequestsService, ServiceRequestDto } from '../../core/services/service-requests.service';
import { PurchaseOrdersService, CreatePurchaseOrderRequest } from '../../core/services/purchase-orders.service';
import { JobCardsService, CreateJobCardRequest } from '../../core/services/job-cards.service';
import { PartsService, PartDto } from '../../core/services/parts.service';
import { PermitTypesService, PermitTypeDto } from '../../core/services/permit-types.service';
import { JobCardWorkService } from '../../core/services/job-card-work.service';
import { ToastService } from '../../core/services/toast.service';
import { TableComponent } from '../../shared/table/table.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../shared/table-pagination/table-pagination.component';

type StartType = 'quote' | 'request' | 'existingQuote';
type QuoteInputMode = 'materials' | 'guestimation';
type POSource = 'client' | 'us' | 'later';

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
  selector: 'app-start-new-job',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, BreadcrumbComponent, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './start-new-job.component.html',
  styleUrl: './start-new-job.component.scss'
})
export class StartNewJobComponent implements OnInit {
  step = 1;
  maxStep = 6;

  createdJobCardId: string | null = null;

  // Step 1
  clients: ClientDto[] = [];
  sites: SiteDto[] = [];
  clientId: string | null = null;
  siteId: string | null = null;

  // Step 2
  startType: StartType = 'quote';

  // Step 3
  activeRequests: ServiceRequestDto[] = [];
  selectedRequestId: string | null = null;
  quoteAmount: number | null = null;
  quoteDeferPricing = false;
  quoteDiscountMode: 'None' | 'Global' | 'PerItem' = 'None';
  quoteGlobalDiscountPercent = 0;
  quoteDescription = '';
  quoteValidUntil = '';
  quoteNotes = '';
  createdQuoteId: string | null = null;
  existingQuotes: QuoteDto[] = [];
  selectedExistingQuoteId: string | null = null;
  quoteLineItems: QuoteLineRow[] = [];
  quoteLineItemsPage = 1;
  readonly quoteLineItemsPageSize = 10;
  quoteParts: PartDto[] = [];
  quoteInputMode: QuoteInputMode = 'guestimation';

  // Step 4
  poSource: POSource = 'us';
  clientPONumber = '';
  clientPOFile: File | null = null;
  createdPOId: string | null = null;

  // Step 5: Job setup
  jobDescription = '';
  jobPriority = 3;
  jobDueDate = '';
  permitTypes: PermitTypeDto[] = [];
  assignableTechnicians: { userId: string; userName?: string; badgeIds?: string[] }[] = [];
  requiredBadgeIds: string[] = [];
  requiredBadgeNames: string[] = [];
  assignUserId = '';
  selectedTechnicianIds: string[] = [];
  permitManagerUserId: string | null = null;

  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private clientsService: ClientsService,
    private sitesService: SitesService,
    private quotesService: QuotesService,
    private serviceRequestsService: ServiceRequestsService,
    private purchaseOrdersService: PurchaseOrdersService,
    private jobCardsService: JobCardsService,
    private partsService: PartsService,
    private permitTypesService: PermitTypesService,
    private jobCardWorkService: JobCardWorkService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['newClientId']) this.clientId = q['newClientId'];
    if (q['newSiteId']) this.siteId = q['newSiteId'];
    if (q['clientId']) this.clientId = q['clientId'];
    if (q['siteId']) this.siteId = q['siteId'];
    if (q['jobCardId']) this.createdJobCardId = q['jobCardId'];
    if (q['startType'] && ['quote', 'request', 'existingQuote'].includes(q['startType'])) this.startType = q['startType'];
    if (q['step']) {
      const s = Number(q['step']);
      if (s >= 1 && s <= this.maxStep) this.step = s;
    }
    this.loadClientsAndSites();
    if (this.createdJobCardId && (!this.clientId || !this.siteId)) {
      this.jobCardsService.get(this.createdJobCardId).subscribe({
        next: (job) => {
          if (job.companyId) this.clientId = job.companyId;
          this.siteId = job.siteId;
          if (this.clientId) this.loadSitesForClient(this.clientId);
        }
      });
    }
    if (this.step === 2 && this.startType === 'request') {
      this.loadActiveRequestsForAllSites();
    }
    if (this.step === 2 && this.startType === 'existingQuote' && this.clientId && this.siteId) {
      this.loadExistingQuotes();
    }
    if (this.step === 2 && this.startType === 'quote') {
      this.loadQuoteParts();
    }
    if (this.step === 3 && this.startType === 'request') {
      this.loadQuoteParts();
    }
    if (this.step === 5) {
      this.loadPermitTypes();
      if (this.createdJobCardId) this.loadAssignableTechnicians();
    }
  }

  private updateUrl(): void {
    const params: Record<string, string> = { step: String(this.step) };
    if (this.createdJobCardId) params['jobCardId'] = this.createdJobCardId;
    if (this.clientId) params['clientId'] = this.clientId;
    if (this.siteId) params['siteId'] = this.siteId;
    this.router.navigate([], { relativeTo: this.route, queryParams: params, queryParamsHandling: '' });
  }

  get returnToForJobSetup(): string {
    const params = new URLSearchParams();
    if (this.createdJobCardId) params.set('jobCardId', this.createdJobCardId);
    if (this.clientId) params.set('clientId', this.clientId);
    if (this.siteId) params.set('siteId', this.siteId);
    params.set('step', '5');
    return '/start-new-job?' + params.toString();
  }

  get returnToForQuote(): string {
    const params = new URLSearchParams();
    if (this.createdJobCardId) params.set('jobCardId', this.createdJobCardId);
    if (this.clientId) params.set('clientId', this.clientId);
    if (this.siteId) params.set('siteId', this.siteId);
    params.set('step', this.startType === 'request' ? '3' : '2');
    params.set('startType', this.startType);
    return '/start-new-job?' + params.toString();
  }

  get returnToBase(): string {
    const params = new URLSearchParams();
    if (this.createdJobCardId) params.set('jobCardId', this.createdJobCardId);
    if (this.clientId) params.set('clientId', this.clientId);
    if (this.siteId) params.set('siteId', this.siteId);
    return '/start-new-job?' + params.toString();
  }

  loadClientsAndSites(): void {
    this.loading = true;
    this.clientsService.list(true).subscribe({
      next: (clients) => {
        this.clients = clients;
        if (this.clientId) this.loadSitesForClient(this.clientId);
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  onClientChange(): void {
    this.siteId = null;
    this.sites = [];
    if (this.clientId) this.loadSitesForClient(this.clientId);
  }

  loadSitesForClient(clientId: string): void {
    this.sitesService.list(clientId, true).subscribe({
      next: (sites) => (this.sites = sites)
    });
  }

  loadActiveRequests(): void {
    if (!this.siteId) return;
    this.loading = true;
    this.serviceRequestsService.list(this.siteId).subscribe({
      next: (list) => {
        this.activeRequests = list.filter(r => !r.jobCardId);
        this.selectedRequestId = this.activeRequests.length ? this.activeRequests[0].id : null;
        this.onSelectedRequestChange();
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  onSelectedRequestChange(): void {
    const r = this.activeRequests.find(x => x.id === this.selectedRequestId);
    if (r) this.quoteDescription = r.description ?? '';
  }

  get selectedRequest(): ServiceRequestDto | undefined {
    return this.activeRequests.find(r => r.id === this.selectedRequestId);
  }

  loadExistingQuotes(): void {
    if (!this.clientId || !this.siteId) return;
    this.loading = true;
    this.quotesService.list(this.clientId, this.siteId).subscribe({
      next: (quotes) => {
        this.existingQuotes = quotes.filter(q => !q.linkedPurchaseOrderId);
        this.selectedExistingQuoteId = this.existingQuotes.length ? this.existingQuotes[0].id : null;
        const q = this.existingQuotes.find(x => x.id === this.selectedExistingQuoteId);
        if (q) this.quoteAmount = q.amount;
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  loadQuoteParts(): void {
    if (this.startType === 'existingQuote') return;
    this.partsService.list().subscribe({ next: (p) => (this.quoteParts = p) });
  }

  addQuoteLineItem(lineType: 'Labour' | 'Part'): void {
    this.quoteLineItems.push({
      lineType,
      description: '',
      unit: '',
      quantity: 1,
      unitPrice: 1,
      discountPercent: 0
    });
    this.quoteLineItemsPage = Math.max(1, Math.ceil(this.quoteLineItems.length / this.quoteLineItemsPageSize));
  }

  /** Parts available for the given line type (Labour parts for Labour, stock parts for Part). */
  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.quoteParts.filter(p => !!p.isLabour === labour);
  }

  /** Parts (non-labour) in quote where qty exceeds stock. */
  get partsInsufficientInQuote(): { partName: string; need: number; have: number }[] {
    const result: { partName: string; need: number; have: number }[] = [];
    for (const li of this.quoteLineItems) {
      if (li.lineType !== 'Part' || !li.partId || li.quantity <= 0) continue;
      const part = this.quoteParts.find(p => p.id === li.partId);
      if (!part || part.isLabour) continue;
      const need = Math.round(Number(li.quantity)) || 0;
      if (need > part.quantity) {
        result.push({ partName: part.name, need, have: part.quantity });
      }
    }
    return result;
  }

  removeQuoteLineItem(idx: number): void {
    this.quoteLineItems.splice(idx, 1);
    const maxPage = Math.max(1, Math.ceil(this.quoteLineItems.length / this.quoteLineItemsPageSize));
    this.quoteLineItemsPage = Math.min(this.quoteLineItemsPage, maxPage);
  }

  onQuotePartSelect(idx: number, partId: string | null): void {
    const row = this.quoteLineItems[idx];
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

  get quoteTotalFromLines(): number {
    const valid = this.quoteLineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const subtotal = valid.reduce((s, li) => s + li.quantity * li.unitPrice, 0);
    const perItemDiscount = this.quoteDiscountMode === 'PerItem'
      ? valid.reduce((s, li) => s + ((li.quantity * li.unitPrice) * this.clampPercent(li.discountPercent) / 100), 0)
      : 0;
    const afterPerItem = subtotal - perItemDiscount;
    const globalDiscount = this.quoteDiscountMode === 'Global'
      ? afterPerItem * this.clampPercent(this.quoteGlobalDiscountPercent) / 100
      : 0;
    return Math.max(0, afterPerItem - globalDiscount);
  }

  get quoteSubtotalFromLines(): number {
    return this.quoteLineItems
      .filter(li => li.quantity > 0 && li.unitPrice >= 0)
      .reduce((s, li) => s + li.quantity * li.unitPrice, 0);
  }

  get quoteDiscountFromLines(): number {
    return Math.max(0, this.quoteSubtotalFromLines - this.quoteTotalFromLines);
  }

  get showPerItemDiscountColumn(): boolean {
    return this.quoteDiscountMode === 'PerItem';
  }

  quoteLineSubtotal(li: QuoteLineRow): number {
    return Math.max(0, li.quantity * li.unitPrice);
  }

  quoteLineDiscountAmount(li: QuoteLineRow): number {
    if (this.quoteDiscountMode !== 'PerItem') return 0;
    return this.quoteLineSubtotal(li) * this.clampPercent(li.discountPercent) / 100;
  }

  quoteLineDiscountedTotal(li: QuoteLineRow): number {
    return Math.max(0, this.quoteLineSubtotal(li) - this.quoteLineDiscountAmount(li));
  }

  private clampPercent(value: number | null | undefined): number {
    const n = Number(value ?? 0);
    if (!Number.isFinite(n)) return 0;
    return Math.min(100, Math.max(0, n));
  }

  get effectiveQuoteAmount(): number {
    if (this.quoteInputMode === 'materials') {
      const fromLines = this.quoteLineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
      if (fromLines.length > 0) {
        return this.quoteTotalFromLines;
      }
    }
    const base = this.quoteDeferPricing ? 0 : (this.quoteAmount ?? 0);
    if (this.quoteDiscountMode !== 'Global') return base;
    return Math.max(0, base - (base * this.clampPercent(this.quoteGlobalDiscountPercent) / 100));
  }

  loadPermitTypes(): void {
    this.permitTypesService.list(true).subscribe({ next: (list) => (this.permitTypes = list) });
  }

  onQuoteInputModeChange(mode: QuoteInputMode): void {
    this.quoteInputMode = mode;
    if (mode === 'materials') {
      this.quoteAmount = null;
      this.quoteDeferPricing = false;
    } else {
      this.quoteLineItems = [];
      this.quoteLineItemsPage = 1;
      if (this.quoteDiscountMode === 'PerItem') this.quoteDiscountMode = 'None';
    }
  }

  onQuoteDiscountModeChange(mode: 'None' | 'Global' | 'PerItem'): void {
    this.quoteDiscountMode = mode;
    if (mode !== 'Global') this.quoteGlobalDiscountPercent = 0;
    if (mode !== 'PerItem') {
      this.quoteLineItems = this.quoteLineItems.map(li => ({ ...li, discountPercent: 0 }));
    }
    if (this.quoteInputMode !== 'materials' && mode === 'PerItem') {
      this.quoteDiscountMode = 'None';
    }
  }

  nextFromStep1(): void {
    this.error = null;
    this.step = 2;
    this.updateUrl();
    if (this.startType === 'request') {
      this.loadActiveRequestsForAllSites();
    }
    if (this.startType === 'existingQuote') {
      this.loadClientsAndSites();
    }
    if (this.startType === 'quote') {
      this.loadClientsAndSites();
      this.loadQuoteParts();
    }
  }

  private loadActiveRequestsForAllSites(): void {
    this.loading = true;
    this.serviceRequestsService.list().subscribe({
      next: (list) => {
        this.activeRequests = list.filter(r => !r.jobCardId);
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  nextFromStep2(): void {
    this.error = null;
    if (this.startType === 'request') {
      if (!this.selectedRequestId) {
        this.error = 'Select a service request.';
        return;
      }
      const req = this.activeRequests.find(r => r.id === this.selectedRequestId);
      if (!req?.siteId) {
        this.error = 'Selected request has no site.';
        return;
      }
      this.clientId = req.companyId ?? null;
      this.siteId = req.siteId;
      if (this.clientId) this.loadSitesForClient(this.clientId);
      this.submitting = true;
      this.jobCardsService.create({ siteId: req.siteId, serviceRequestId: this.selectedRequestId, status: 'Draft' }).subscribe({
        next: (job) => {
          this.createdJobCardId = job.id;
          this.quoteDescription = req.description ?? '';
          this.loadQuoteParts();
          this.step = 3;
          this.submitting = false;
          this.updateUrl();
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to create job card.';
          this.submitting = false;
        }
      });
      return;
    }
    if (this.startType === 'existingQuote') {
      if (!this.clientId || !this.siteId) {
        this.error = 'Select client and site.';
        return;
      }
      if (!this.selectedExistingQuoteId) {
        this.error = 'Select an existing quote.';
        return;
      }
      const selected = this.existingQuotes.find(q => q.id === this.selectedExistingQuoteId);
      if (selected) this.quoteAmount = selected.amount;
      this.submitting = true;
      this.jobCardsService.create({ siteId: this.siteId!, status: 'Draft' }).subscribe({
        next: (job) => {
          this.createdJobCardId = job.id;
          this.createdQuoteId = this.selectedExistingQuoteId!;
          this.quotesService.linkToJobCard(this.selectedExistingQuoteId!, job.id).subscribe({
            next: () => {
              this.submitting = false;
              this.step = 4;
              this.updateUrl();
            },
            error: (err) => {
              this.error = err.error?.message || 'Failed to link quote to job card.';
              this.submitting = false;
            }
          });
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to create job card.';
          this.submitting = false;
        }
      });
      return;
    }
    if (this.startType === 'quote') {
      if (!this.clientId || !this.siteId) {
        this.error = 'Select client and site.';
        return;
      }
      if (!this.canProceedQuoteStep()) {
        this.error = 'Complete the quote section.';
        return;
      }
      this.submitting = true;
      this.jobCardsService.create({ siteId: this.siteId!, status: 'Draft' }).subscribe({
        next: (job) => {
          this.createdJobCardId = job.id;
          this.createQuote(undefined);
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to create job card.';
          this.submitting = false;
        }
      });
    }
  }

  canProceedQuoteStep(): boolean {
    if (!this.quoteDescription?.trim()) return false;
    if (this.quoteInputMode === 'materials') {
      const valid = this.quoteLineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
      if (valid.length === 0) return false;
      if (valid.some(li => li.lineType === 'Part' && !li.partId)) return false;
      if (valid.some(li => !li.description?.trim())) return false;
      return true;
    }
    return this.quoteDeferPricing || (this.quoteAmount != null && this.quoteAmount >= 0);
  }

  onQuoteClientOrSiteChange(): void {
    if (this.startType === 'existingQuote' && this.clientId && this.siteId) {
      this.loadExistingQuotes();
    }
    if (this.startType === 'quote' && this.clientId) {
      this.loadSitesForClient(this.clientId);
    }
    if (this.startType === 'quote' && this.siteId) {
      this.loadQuoteParts();
    }
  }

  nextFromStep3(): void {
    this.error = null;
    if (this.startType === 'existingQuote') {
      if (!this.selectedExistingQuoteId) {
        this.error = 'Select an existing quote.';
        return;
      }
      if (!this.createdJobCardId) {
        this.error = 'Job card is missing.';
        return;
      }
      const selected = this.existingQuotes.find(q => q.id === this.selectedExistingQuoteId);
      this.createdQuoteId = this.selectedExistingQuoteId;
      if (selected) this.quoteAmount = selected.amount;
      this.submitting = true;
      this.quotesService.linkToJobCard(this.selectedExistingQuoteId, this.createdJobCardId).subscribe({
        next: () => {
          this.submitting = false;
          this.step = 4;
          this.updateUrl();
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to link quote to job card.';
          this.submitting = false;
        }
      });
      return;
    }
    if (this.startType === 'request') {
      if (!this.selectedRequestId) {
        this.error = 'Select a service request.';
        return;
      }
      if (!this.createdJobCardId) {
        this.error = 'Job card is missing.';
        return;
      }
      this.submitting = true;
      this.jobCardsService.update(this.createdJobCardId, { serviceRequestId: this.selectedRequestId }).subscribe({
        next: () => {
          this.createQuote(this.selectedRequestId!);
        },
        error: (err) => {
          this.error = err.error?.message || 'Failed to link request to job card.';
          this.submitting = false;
        }
      });
    } else {
      this.createQuote(undefined);
    }
  }

  onExistingQuoteChange(): void {
    const q = this.existingQuotes.find(x => x.id === this.selectedExistingQuoteId);
    if (q) this.quoteAmount = q.amount;
  }

  private createQuote(serviceRequestId: string | undefined): void {
    if (!this.quoteDescription?.trim()) {
      this.error = 'Quote description is required.';
      this.submitting = false;
      return;
    }
    const useMaterials = this.quoteInputMode === 'materials';
    const validLines = this.quoteLineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const hasValidLines = useMaterials && validLines.length > 0;
    const amt = hasValidLines ? this.quoteTotalFromLines : this.effectiveQuoteAmount;
    if (useMaterials && !hasValidLines) {
      this.error = 'Add at least one line item with quantity and price when building with materials.';
      this.submitting = false;
      return;
    }
    if (useMaterials && validLines.some(li => li.lineType === 'Part' && !li.partId)) {
      this.error = 'Each Part line must select a part item so it can attach to the job.';
      this.submitting = false;
      return;
    }
    if (useMaterials && validLines.some(li => !li.description?.trim())) {
      this.error = 'Each quote line must include a description.';
      this.submitting = false;
      return;
    }
    if (!useMaterials && !this.quoteDeferPricing && (this.quoteAmount == null || this.quoteAmount < 0)) {
      this.error = 'Enter amount or check "Sort price later".';
      this.submitting = false;
      return;
    }
    const lineItems: QuoteLineItemInput[] | undefined = hasValidLines
      ? validLines.map(li => ({
          lineType: li.lineType,
          description: li.description,
          quantity: Math.round(Number(li.quantity)) || 0,
          unitPrice: li.unitPrice,
          discountPercent: this.quoteDiscountMode === 'PerItem' ? this.clampPercent(li.discountPercent) : 0,
          partId: li.partId
        }))
      : undefined;
    const body: CreateQuoteRequest = {
      clientId: this.clientId!,
      siteId: this.siteId!,
      serviceRequestId,
      amount: amt,
      deferPricing: this.quoteDeferPricing,
      discountMode: this.quoteDiscountMode,
      globalDiscountPercent: this.quoteDiscountMode === 'Global' ? this.clampPercent(this.quoteGlobalDiscountPercent) : 0,
      currency: 'ZAR',
      description: this.quoteDescription.trim(),
      notes: this.quoteNotes.trim() || undefined,
      validUntil: this.quoteValidUntil || undefined,
      lineItems
    };
    if (this.createdJobCardId) body.jobCardId = this.createdJobCardId;
    this.quotesService.create(body).subscribe({
      next: (quote) => {
        this.createdQuoteId = quote.id;
        this.submitting = false;
        this.step = 4;
        this.updateUrl();
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to create quote.';
        this.submitting = false;
      }
    });
  }

  onClientPOFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.clientPOFile = input.files?.[0] ?? null;
  }

  openDatePicker(event: Event): void {
    const target = event.target as HTMLInputElement & { showPicker?: () => void };
    target.showPicker?.();
  }

  nextFromStep4(): void {
    this.error = null;
    if (!this.createdQuoteId) {
      this.error = 'Quote is missing.';
      return;
    }
    if (this.poSource === 'later') {
      this.finishStep4();
      return;
    }
    const poBody: CreatePurchaseOrderRequest = {
      clientId: this.clientId!,
      siteId: this.siteId!,
      quoteId: this.createdQuoteId,
      amount: this.effectiveQuoteAmount,
      currency: 'ZAR',
      jobCardId: this.createdJobCardId || undefined,
      clientPONumber: this.poSource === 'client' && this.clientPONumber.trim() ? this.clientPONumber.trim() : undefined,
      notes: undefined
    };
    this.submitting = true;
    this.purchaseOrdersService.create(poBody).subscribe({
      next: (po) => {
        this.createdPOId = po.id;
        if (this.poSource === 'client' && this.clientPOFile) {
          this.purchaseOrdersService.uploadClientPO(po.id, this.clientPOFile).subscribe({
            next: () => {
              this.toast.success('Purchase order and file saved.');
              this.finishStep4();
            },
            error: (err) => {
              this.toast.error(err.error?.message || 'PO created but file upload failed. You can add the file on the PO page.');
              this.finishStep4();
            }
          });
        } else {
          this.finishStep4();
        }
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to create purchase order.';
        this.submitting = false;
      }
    });
  }

  private finishStep4(): void {
    this.submitting = false;
    this.step = 5;
    this.loadPermitTypes();
    this.updateUrl();
    if (this.createdJobCardId) this.loadAssignableTechnicians();
  }

  private loadAssignableTechnicians(): void {
    if (!this.createdJobCardId) return;
    this.jobCardWorkService.getAssignableTechnicians(this.createdJobCardId).subscribe({
      next: (res) => {
        this.assignableTechnicians = res.technicians;
        this.requiredBadgeIds = res.requiredBadgeIds ?? [];
        this.requiredBadgeNames = res.requiredBadgeNames ?? [];
      }
    });
  }

  technicianLacksRequiredBadges(t: { badgeIds?: string[] }): boolean {
    if (!this.requiredBadgeIds.length) return false;
    const ids = t.badgeIds ?? [];
    return this.requiredBadgeIds.some(rid => !ids.includes(rid));
  }

  isTechnicianSelected(userId: string): boolean {
    return this.selectedTechnicianIds.includes(userId);
  }

  nextFromStep5(): void {
    this.step = 6;
    this.updateUrl();
  }

  createJobAndFinish(): void {
    this.error = null;
    if (!this.createdJobCardId) {
      this.error = 'Job card is missing.';
      return;
    }
    if (this.jobDueDate) {
      const today = new Date();
      today.setHours(0, 0, 0, 0);
      const due = new Date(this.jobDueDate);
      due.setHours(0, 0, 0, 0);
      if (due < today) {
        this.error = 'Job due date cannot be in the past.';
        return;
      }
      if (this.quoteValidUntil) {
        const quoteDate = new Date(this.quoteValidUntil);
        quoteDate.setHours(0, 0, 0, 0);
        if (quoteDate > due) {
          this.error = 'Quote valid-until date must be on or before the job due date.';
          return;
        }
      }
    }
    this.submitting = true;
    const workAuthPermitTypeId = this.getWorkAuthorisationPermitTypeId();
    this.jobCardsService.update(this.createdJobCardId, {
      status: 'Open',
      description: this.jobDescription?.trim() || undefined,
      priority: this.jobPriority,
      dueDate: this.jobDueDate || null,
      permitsRequired: true,
      requiredPermitTypeId: workAuthPermitTypeId ?? undefined,
      partsRequired: false,
      plannedParts: []
    }).subscribe({
      next: () => {
        this.sendQuoteToClientThenFinish();
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to complete setup.';
        this.submitting = false;
      }
    });
  }

  /** After setup is saved, email the quote (all quote shapes: guestimate, defer pricing, line items). Ignores if already Sent. */
  private sendQuoteToClientThenFinish(): void {
    if (!this.createdQuoteId) {
      this.assignTechniciansAndNavigate();
      return;
    }
    this.quotesService.send(this.createdQuoteId, undefined, true).subscribe({
      next: () => {
        this.toast.success('Quote sent to the client by email.');
        this.assignTechniciansAndNavigate();
      },
      error: (err) => {
        const msg = (err.error?.message || err.error || '').toString();
        if (msg.includes('Cannot transition') || msg.includes('transition')) {
          this.assignTechniciansAndNavigate();
          return;
        }
        this.toast.info('Job saved, but the quote could not be emailed. Send it from the quote page when ready.');
        this.assignTechniciansAndNavigate();
      }
    });
  }

  get clientName(): string {
    return this.clients.find(c => c.id === this.clientId)?.companyName ?? '—';
  }

  get siteName(): string {
    return this.sites.find(s => s.id === this.siteId)?.name ?? '—';
  }

  get quoteSummary(): string {
    if (this.quoteDeferPricing) return 'Sort price later · ' + ((this.quoteDescription || '').slice(0, 40) + ((this.quoteDescription?.length ?? 0) > 40 ? '…' : ''));
    const amt = this.effectiveQuoteAmount;
    const modeNote = this.quoteDiscountMode === 'Global'
      ? ` · Global discount ${this.clampPercent(this.quoteGlobalDiscountPercent).toFixed(2)}%`
      : this.quoteDiscountMode === 'PerItem'
        ? ' · Per-item discounts'
        : '';
    return `R${amt.toLocaleString('en-ZA', { minimumFractionDigits: 2 })}${modeNote} · ${(this.quoteDescription || '').slice(0, 40)}${(this.quoteDescription?.length ?? 0) > 40 ? '…' : ''}`;
  }

  /** Work Authorisation permit type for first permit. */
  private getWorkAuthorisationPermitTypeId(): string | null {
    const workAuth = this.permitTypes.find(p => p.isWorkAuthorisation);
    if (workAuth) return workAuth.id;
    return this.permitTypes.length > 0 ? this.permitTypes[0].id : null;
  }

  get selectedJobTypeName(): string {
    return '—';
  }

  get techniciansSummary(): string {
    if (!this.selectedTechnicianIds.length) return 'None selected';
    const names = this.selectedTechnicianIds
      .map(id => this.assignableTechnicians.find(t => t.userId === id)?.userName ?? id)
      .join(', ');
    const permManager = this.selectedTechnicianIds[0];
    if (permManager) {
      const pm = this.assignableTechnicians.find(t => t.userId === permManager);
      return names + (pm ? ` (Permit manager: ${pm.userName ?? pm.userId})` : '');
    }
    return names;
  }

  private assignTechniciansAndNavigate(): void {
    if (!this.createdJobCardId || this.selectedTechnicianIds.length === 0) {
      this.submitting = false;
      this.router.navigate(['/job-cards', this.createdJobCardId!]);
      return;
    }
    const permManager = this.permitManagerUserId ?? this.selectedTechnicianIds[0];
    let assigned = 0;
    const total = this.selectedTechnicianIds.length;
    const onDone = () => {
      assigned++;
      if (assigned >= total) {
        this.submitting = false;
        this.router.navigate(['/job-cards', this.createdJobCardId!]);
      }
    };
    this.selectedTechnicianIds.forEach(uid => {
      this.jobCardWorkService.assignTechnician(this.createdJobCardId!, uid, uid === permManager).subscribe({
        next: () => onDone(),
        error: () => onDone()
      });
    });
  }

  back(): void {
    this.error = null;
    if (this.step > 1) {
      this.step--;
      this.updateUrl();
      if (this.step === 2 && this.startType === 'request') {
        this.loadActiveRequestsForAllSites();
      }
      if (this.step === 2 && this.startType === 'existingQuote' && this.clientId && this.siteId) {
        this.loadExistingQuotes();
      }
      if ((this.step === 2 || this.step === 3) && this.startType === 'quote') {
        this.loadQuoteParts();
      }
      if (this.step === 3 && this.startType === 'request') {
        this.loadQuoteParts();
      }
      if (this.step === 5) {
        this.loadPermitTypes();
        if (this.createdJobCardId) this.loadAssignableTechnicians();
      }
    }
  }

  goToStep(s: number): void {
    if (s >= 1 && s < this.step) {
      this.step = s;
      this.updateUrl();
      if (this.step === 2 && this.startType === 'request') {
        this.loadActiveRequestsForAllSites();
      }
      if (this.step === 2 && this.startType === 'existingQuote' && this.clientId && this.siteId) {
        this.loadExistingQuotes();
      }
      if ((this.step === 2 || this.step === 3) && this.startType === 'quote') {
        this.loadQuoteParts();
      }
      if (this.step === 3 && this.startType === 'request') {
        this.loadQuoteParts();
      }
      if (this.step === 5) {
        this.loadPermitTypes();
        if (this.createdJobCardId) this.loadAssignableTechnicians();
      }
    }
  }

  get stepLabels(): string[] {
    return ['How did this start?', 'Quote or select request', 'Quote details', 'Purchase order', 'Job setup', 'Review & complete'];
  }

  /** Selected technicians as objects for the selected list. */
  get selectedTechniciansList(): { userId: string; userName?: string; badgeIds?: string[] }[] {
    return this.selectedTechnicianIds
      .map(id => this.assignableTechnicians.find(t => t.userId === id))
      .filter((t): t is { userId: string; userName?: string; badgeIds?: string[] } => !!t);
  }

  /** Technicians still available to add (not yet selected). */
  get availableTechniciansToAdd(): { userId: string; userName?: string; badgeIds?: string[] }[] {
    return this.assignableTechnicians.filter(t => !this.selectedTechnicianIds.includes(t.userId));
  }

  removeTechnician(userId: string): void {
    this.selectedTechnicianIds = this.selectedTechnicianIds.filter(id => id !== userId);
    if (this.permitManagerUserId === userId) this.permitManagerUserId = null;
  }

  addTechnician(userId: string): void {
    if (!this.selectedTechnicianIds.includes(userId)) {
      this.selectedTechnicianIds = [...this.selectedTechnicianIds, userId];
    }
  }

  /** True when due date is set and strictly before today (local midnight). */
  get jobDueDateInPast(): boolean {
    if (!this.jobDueDate) return false;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const due = new Date(this.jobDueDate);
    due.setHours(0, 0, 0, 0);
    return due < today;
  }

  /** True when quote valid-until is after the chosen job due date (both dates compared at local midnight). */
  get jobDueDateBeforeQuoteValidUntil(): boolean {
    if (!this.jobDueDate || !this.quoteValidUntil) return false;
    const due = new Date(this.jobDueDate);
    due.setHours(0, 0, 0, 0);
    const quoteEnd = new Date(this.quoteValidUntil);
    quoteEnd.setHours(0, 0, 0, 0);
    return quoteEnd > due;
  }

  get canProceedFromJobSetup(): boolean {
    if (!this.selectedTechnicianIds.length) return false;
    if (this.jobDueDateInPast || this.jobDueDateBeforeQuoteValidUntil) return false;
    return true;
  }

  get canCompleteJobSetup(): boolean {
    return this.canProceedFromJobSetup;
  }
}
