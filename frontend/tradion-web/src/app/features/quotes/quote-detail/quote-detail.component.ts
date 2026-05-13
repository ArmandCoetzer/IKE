import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { QuotesService, QuoteDto } from '../../../core/services/quotes.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { AuthService } from '../../../core/services/auth.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, BreadcrumbComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './quote-detail.component.html',
  styleUrl: './quote-detail.component.scss'
})
export class QuoteDetailComponent implements OnInit {
  item: QuoteDto | null = null;
  loading = true;
  error: string | null = null;
  actionError: string | null = null;
  sending = false;
  accepting = false;
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private quotesService: QuotesService,
    private documentsService: DocumentsService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.quotesService.get(id).subscribe({
      next: (q) => {
        this.item = q;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load quote.';
        this.loading = false;
      }
    });
  }

  downloadPdf(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.documentsService.getQuotePdf(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Quote-${this.item?.quoteNumber ?? id}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: () => (this.actionError = 'Download failed.')
    });
  }

  get isClientUser(): boolean {
    return (this.auth.role() || '').trim().toLowerCase() === 'client';
  }

  acceptByClient(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.accepting = true;
    this.quotesService.accept(id).subscribe({
      next: () => {
        this.accepting = false;
        this.item = this.item ? { ...this.item, status: 'Accepted' } : null;
      },
      error: (err) => {
        this.actionError = err.error?.message || 'Accept failed.';
        this.accepting = false;
      }
    });
  }

  sendToClient(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.sending = true;
    this.quotesService.send(id, undefined, true).subscribe({
      next: () => {
        this.sending = false;
        this.item = this.item ? { ...this.item, sentAt: new Date().toISOString(), status: 'Sent' } : null;
      },
      error: () => {
        this.actionError = 'Send failed. Check email configuration.';
        this.sending = false;
      }
    });
  }

  updatingStatus = false;
  statusError: string | null = null;

  setStatus(status: string): void {
    if (!this.item?.id) return;
    this.statusError = null;
    this.updatingStatus = true;
    this.quotesService.updateStatus(this.item.id, status).subscribe({
      next: (updated) => {
        this.item = updated;
        this.updatingStatus = false;
      },
      error: (err) => {
        this.statusError = err.error?.message || 'Failed to update status.';
        this.updatingStatus = false;
      }
    });
  }

  get returnTo(): string | null {
    return sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  get createPOQueryParams(): Record<string, string | number> {
    if (!this.item) return {};
    const p: Record<string, string | number> = {
      quoteId: this.item.id,
      clientId: this.item.clientId,
      siteId: this.item.siteId,
      amount: this.item.amount
    };
    const rt = this.returnTo;
    if (rt) p['returnTo'] = rt;
    return p;
  }

  goBack(): void {
    const url = this.returnTo || '/quotes';
    this.router.navigateByUrl(url);
  }

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Finance', path: '/quotes' },
      { label: 'Quotes', path: '/quotes' },
      { label: this.item?.quoteNumber ?? '…' }
    ];
  }

  get sortedLineItems() {
    return [...(this.item?.lineItems ?? [])];
  }

  get hasLineItems(): boolean {
    return this.sortedLineItems.length > 0;
  }

  get showPerItemDiscountDetails(): boolean {
    if (!this.item || this.item.discountMode !== 'PerItem') return false;
    return (this.item.lineItems ?? []).some(li => (li.discountPercent ?? 0) > 0);
  }
}
