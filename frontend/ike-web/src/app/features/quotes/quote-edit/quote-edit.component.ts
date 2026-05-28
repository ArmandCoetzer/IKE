import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { QuotesService, QuoteDto, UpdateQuoteRequest } from '../../../core/services/quotes.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';

interface QuoteEditLineRow {
  lineType: string;
  description: string;
  unit?: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
}

@Component({
  selector: 'app-quote-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './quote-edit.component.html',
  styleUrl: './quote-edit.component.scss'
})
export class QuoteEditComponent implements OnInit {
  id: string | null = null;
  item: QuoteDto | null = null;
  amount = 0;
  currency = 'ZAR';
  description = '';
  discountMode: 'None' | 'Global' | 'PerItem' = 'None';
  globalDiscountPercent = 0;
  notes = '';
  validUntil = '';
  lineItems: QuoteEditLineRow[] = [];
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;
  quoteParts: PartDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private quotesService: QuotesService,
    private partsService: PartsService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.loading = true;
    this.quotesService.get(this.id).subscribe({
      next: (q) => {
        this.item = q;
        if (q.isUploaded) {
          this.error = 'Uploaded quotes are read-only and cannot be edited.';
          this.loading = false;
          return;
        }
        this.amount = q.amount ?? 0;
        this.currency = q.currency ?? 'ZAR';
        this.description = q.description ?? '';
        this.discountMode = (q.discountMode as 'None' | 'Global' | 'PerItem') ?? 'None';
        this.globalDiscountPercent = q.globalDiscountPercent ?? 0;
        this.notes = q.notes ?? '';
        this.validUntil = q.validUntil ? q.validUntil.toString().slice(0, 10) : '';
        this.lineItems = (q.lineItems ?? []).map(li => ({
          lineType: li.lineType || 'Labour',
          description: li.description || '',
          unit: '',
          quantity: li.quantity ?? 0,
          unitPrice: li.unitPrice ?? 0,
          discountPercent: li.discountPercent ?? 0,
          partId: li.partId
        }));
        this.loadQuoteParts();
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load quote.';
        this.loading = false;
      }
    });
  }

  loadQuoteParts(): void {
    this.partsService.list().subscribe({
      next: (parts) => {
        this.quoteParts = parts;
        this.lineItems = this.lineItems.map(li => {
          if (!li.partId) return { ...li, unit: li.unit || '' };
          const part = parts.find(p => p.id === li.partId);
          return { ...li, unit: part?.unit?.trim() || '' };
        });
      }
    });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.quoteParts.filter(p => !!p.isLabour === labour);
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
    if (!partId) {
      row.description = '';
      row.unit = '';
      return;
    }
    const p = this.quoteParts.find(x => x.id === partId);
    if (!p) return;
    row.description = p.description?.trim() || '';
    row.unit = p.unit?.trim() || '';
    if (p.unitPrice != null) row.unitPrice = p.unitPrice;
  }

  get quoteTotalFromLines(): number {
    const valid = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    const subtotal = valid.reduce((sum, li) => sum + li.quantity * li.unitPrice, 0);
    const perItemDiscount = this.discountMode === 'PerItem'
      ? valid.reduce((sum, li) => sum + ((li.quantity * li.unitPrice) * this.clampPercent(li.discountPercent) / 100), 0)
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
      .reduce((sum, li) => sum + li.quantity * li.unitPrice, 0);
  }

  get quoteDiscountFromLines(): number {
    return Math.max(0, this.quoteSubtotalFromLines - this.quoteTotalFromLines);
  }

  get showPerItemDiscountColumn(): boolean {
    return this.discountMode === 'PerItem';
  }

  lineSubtotal(li: QuoteEditLineRow): number {
    return Math.max(0, li.quantity * li.unitPrice);
  }

  lineDiscountAmount(li: QuoteEditLineRow): number {
    if (this.discountMode !== 'PerItem') return 0;
    return this.lineSubtotal(li) * this.clampPercent(li.discountPercent) / 100;
  }

  lineDiscountedTotal(li: QuoteEditLineRow): number {
    return Math.max(0, this.lineSubtotal(li) - this.lineDiscountAmount(li));
  }

  get hasValidLineItems(): boolean {
    return this.lineItems.some(li => li.quantity > 0 && li.unitPrice >= 0);
  }

  private clampPercent(value: number | null | undefined): number {
    const n = Number(value ?? 0);
    if (!Number.isFinite(n)) return 0;
    return Math.min(100, Math.max(0, n));
  }

  onDiscountModeChange(mode: 'None' | 'Global' | 'PerItem'): void {
    this.discountMode = mode;
    if (mode !== 'Global') this.globalDiscountPercent = 0;
    if (mode !== 'PerItem') {
      this.lineItems = this.lineItems.map(li => ({ ...li, discountPercent: 0 }));
    }
  }

  save(): void {
    if (!this.id) return;
    this.error = null;
    if (!this.description.trim()) {
      this.error = 'Description is required.';
      return;
    }
    const validLines = this.lineItems.filter(li => li.quantity > 0 && li.unitPrice >= 0);
    if (this.lineItems.length > 0 && validLines.length === 0 && this.amount < 0) {
      this.error = 'Amount cannot be negative.';
      return;
    }
    if (validLines.some(li => li.lineType === 'Part' && !li.partId)) {
      this.error = 'Each Part line must select a part item.';
      return;
    }
    if (validLines.some(li => !li.description?.trim())) {
      this.error = 'Each line item must include a description.';
      return;
    }
    this.submitting = true;
    const hasValidLines = validLines.length > 0;
    const body: UpdateQuoteRequest = {
      amount: hasValidLines ? this.quoteTotalFromLines : this.amount,
      currency: this.currency,
      description: this.description.trim(),
      discountMode: this.discountMode,
      globalDiscountPercent: this.discountMode === 'Global' ? this.clampPercent(this.globalDiscountPercent) : 0,
      notes: this.notes.trim() || undefined,
      validUntil: this.validUntil ? this.validUntil : undefined,
      lineItems: this.lineItems.length > 0
        ? validLines.map(li => ({
            lineType: li.lineType,
            description: li.description,
            quantity: li.quantity,
            unitPrice: li.unitPrice,
            discountPercent: this.discountMode === 'PerItem' ? this.clampPercent(li.discountPercent) : 0,
            partId: li.partId
          }))
        : []
    };
    this.quotesService.update(this.id, body).subscribe({
      next: () => this.router.navigate(['/quotes', this.id]),
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update quote.';
      }
    });
  }
}
