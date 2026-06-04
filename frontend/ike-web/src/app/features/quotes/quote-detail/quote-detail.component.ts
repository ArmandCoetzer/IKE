import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { QuotesService, QuoteDto } from '../../../core/services/quotes.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { AuthService } from '../../../core/services/auth.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

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
  uploadedPreviewOpen = false;
  uploadedPreviewLoading = false;
  uploadedPreviewSafeUrl: SafeResourceUrl | null = null;
  uploadedPreviewBlob: Blob | null = null;
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;
  deleting = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private quotesService: QuotesService,
    private documentsService: DocumentsService,
    private sanitizer: DomSanitizer,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
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

  downloadUploadedQuote(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    const existingBlob = this.uploadedPreviewBlob;
    if (existingBlob) {
      this.downloadBlob(existingBlob, this.item?.uploadedFileName || `Uploaded-Quote-${this.item?.quoteNumber ?? id}`);
      return;
    }
    this.quotesService.getUploadedFile(id).subscribe({
      next: (blob) => {
        this.downloadBlob(blob, this.item?.uploadedFileName || `Uploaded-Quote-${this.item?.quoteNumber ?? id}`);
      },
      error: () => (this.actionError = 'Uploaded quote download failed.')
    });
  }

  previewUploadedQuote(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.uploadedPreviewOpen = true;
    if (this.uploadedPreviewSafeUrl) return;
    this.uploadedPreviewLoading = true;
    this.quotesService.previewUploadedFile(id).subscribe({
      next: (blob) => {
        this.uploadedPreviewBlob = blob;
        this.loadUploadedPreviewDataUrl(blob);
      },
      error: () => {
        this.actionError = 'Uploaded quote preview failed.';
        this.uploadedPreviewLoading = false;
        this.uploadedPreviewOpen = false;
      }
    });
  }

  closeUploadedPreview(): void {
    this.uploadedPreviewOpen = false;
  }

  get uploadedPreviewIsImage(): boolean {
    const type = (this.item?.uploadedContentType || '').toLowerCase();
    const name = (this.item?.uploadedFileName || '').toLowerCase();
    return type.startsWith('image/') || /\.(png|jpe?g|webp|gif)$/.test(name);
  }

  private downloadBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }

  private loadUploadedPreviewDataUrl(blob: Blob): void {
    const reader = new FileReader();
    reader.onload = () => {
      this.uploadedPreviewSafeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(String(reader.result || ''));
      this.uploadedPreviewLoading = false;
    };
    reader.onerror = () => {
      this.actionError = 'Uploaded quote preview failed.';
      this.uploadedPreviewLoading = false;
      this.uploadedPreviewOpen = false;
    };
    reader.readAsDataURL(blob);
  }

  get isClientUser(): boolean {
    return (this.auth.role() || '').trim().toLowerCase() === 'client';
  }

  get sendToClientLabel(): string {
    const status = (this.item?.status || '').trim().toLowerCase();
    const wasSent = !!this.item?.sentAt || status === 'sent' || status === 'accepted';
    return wasSent ? 'Send to client again' : 'Send to client';
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

  async deleteQuote(): Promise<void> {
    const item = this.item;
    if (!item?.id || this.deleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete quote',
      message: `Delete quote "${item.quoteNumber}"? Safe links will be detached first and this cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.actionError = null;
    this.deleting = true;
    this.quotesService.delete(item.id).subscribe({
      next: () => {
        this.toast.success('Quote deleted.');
        this.router.navigateByUrl('/quotes');
      },
      error: (err) => {
        this.deleting = false;
        this.actionError = err.error?.message || 'Failed to delete quote.';
        this.toast.error(this.actionError!);
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
    if (!this.item || (this.item.discountMode !== 'PerItem' && this.item.discountMode !== 'PerItemAndGlobal')) return false;
    return (this.item.lineItems ?? []).some(li => (li.discountPercent ?? 0) > 0);
  }
}
