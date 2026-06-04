import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { InvoicesService, InvoiceDto } from '../../../core/services/invoices.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { AuthService } from '../../../core/services/auth.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-invoice-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, BreadcrumbComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './invoice-detail.component.html',
  styleUrl: './invoice-detail.component.scss'
})
export class InvoiceDetailComponent implements OnInit {
  item: InvoiceDto | null = null;
  loading = true;
  error: string | null = null;
  actionError: string | null = null;
  sending = false;
  sendingReminder = false;
  markingPaid = false;
  confirmingParts = false;
  deleting = false;
  uploadedPreviewOpen = false;
  uploadedPreviewLoading = false;
  uploadedPreviewSafeUrl: SafeResourceUrl | null = null;
  uploadedPreviewBlob: Blob | null = null;
  lineItemsPage = 1;
  readonly lineItemsPageSize = 10;
  get subtotalFromLines(): number {
    return (this.item?.lineItems ?? []).reduce((sum, li) => sum + this.lineSubtotal(li), 0);
  }

  get discountFromLines(): number {
    return (this.item?.lineItems ?? []).reduce((sum, li) => sum + this.lineDiscountAmount(li), 0);
  }

  get showDiscountColumns(): boolean {
    return this.discountFromLines > 0;
  }

  lineSubtotal(li: { quantity: number; unitPrice: number }): number {
    return Math.max(0, li.quantity * li.unitPrice);
  }

  lineDiscountAmount(li: { quantity: number; unitPrice: number; discountPercent?: number; lineDiscountAmount?: number }): number {
    if (typeof li.lineDiscountAmount === 'number') return li.lineDiscountAmount;
    const pct = Math.min(100, Math.max(0, Number(li.discountPercent ?? 0)));
    return this.lineSubtotal(li) * pct / 100;
  }

  lineDiscountedTotal(li: { quantity: number; unitPrice: number; discountPercent?: number; lineTotal?: number; lineDiscountAmount?: number }): number {
    if (typeof li.lineTotal === 'number') return li.lineTotal;
    return Math.max(0, this.lineSubtotal(li) - this.lineDiscountAmount(li));
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invoicesService: InvoicesService,
    private documentsService: DocumentsService,
    private sanitizer: DomSanitizer,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  get returnTo(): string | null {
    return sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  goBack(): void {
    this.router.navigateByUrl(this.returnTo || '/invoices');
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.invoicesService.get(id).subscribe({
      next: (i) => {
        this.item = i;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load invoice.';
        this.loading = false;
      }
    });
  }

  downloadPdf(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.documentsService.getInvoicePdf(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Invoice-${this.item?.invoiceNumber ?? id}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: () => (this.actionError = 'Download failed.')
    });
  }

  downloadUploadedInvoice(): void {
    const id = this.item?.id;
    if (!id || !this.item?.isUploaded) return;
    this.actionError = null;
    const existingBlob = this.uploadedPreviewBlob;
    if (existingBlob) {
      this.downloadBlob(existingBlob, this.item?.uploadedFileName || `Uploaded-Invoice-${this.item?.invoiceNumber ?? id}`);
      return;
    }
    this.invoicesService.getUploadedFile(id).subscribe({
      next: (blob) => {
        this.downloadBlob(blob, this.item?.uploadedFileName || `Uploaded-Invoice-${this.item?.invoiceNumber ?? id}`);
      },
      error: () => (this.actionError = 'Uploaded invoice download failed.')
    });
  }

  previewUploadedInvoice(): void {
    const id = this.item?.id;
    if (!id || !this.item?.isUploaded) return;
    this.actionError = null;
    this.uploadedPreviewOpen = true;
    if (this.uploadedPreviewSafeUrl) return;
    this.uploadedPreviewLoading = true;
    this.invoicesService.previewUploadedFile(id).subscribe({
      next: (blob) => {
        this.uploadedPreviewBlob = blob;
        this.loadUploadedPreviewDataUrl(blob);
      },
      error: () => {
        this.actionError = 'Uploaded invoice preview failed.';
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
      this.actionError = 'Uploaded invoice preview failed.';
      this.uploadedPreviewLoading = false;
      this.uploadedPreviewOpen = false;
    };
    reader.readAsDataURL(blob);
  }

  sendToClient(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.sending = true;
    this.invoicesService.send(id, undefined, true).subscribe({
      next: () => {
        this.sending = false;
        this.item = this.item ? { ...this.item, sentAt: new Date().toISOString(), status: this.item.status === 'Paid' ? 'Paid' : 'Sent' } : null;
      },
      error: () => {
        this.actionError = 'Send failed. Check email configuration.';
        this.sending = false;
      }
    });
  }

  sendReminder(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.sendingReminder = true;
    this.invoicesService.sendReminder(id, undefined, true).subscribe({
      next: () => {
        this.sendingReminder = false;
      },
      error: () => {
        this.actionError = 'Send reminder failed. Check email configuration.';
        this.sendingReminder = false;
      }
    });
  }

  markPaid(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.markingPaid = true;
    this.invoicesService.markPaid(id).subscribe({
      next: (updated) => {
        this.item = updated;
        this.markingPaid = false;
      },
      error: () => {
        this.actionError = 'Failed to mark as paid.';
        this.markingPaid = false;
      }
    });
  }

  confirmParts(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.confirmingParts = true;
    const lineItems = this.item?.lineItems?.filter(li => li.quantity > 0).map(li => ({
      lineType: li.lineType,
      description: li.description,
      quantity: li.quantity,
      unitPrice: li.unitPrice,
      discountPercent: li.discountPercent ?? 0,
      partId: li.partId
    }));
    this.invoicesService.confirmParts(id, lineItems?.length ? { lineItems } : undefined).subscribe({
      next: (updated) => {
        this.item = updated;
        this.confirmingParts = false;
      },
      error: () => {
        this.actionError = 'Failed to confirm parts.';
        this.confirmingParts = false;
      }
    });
  }

  async deleteInvoice(): Promise<void> {
    const item = this.item;
    if (!item?.id || this.deleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete invoice',
      message: `Delete invoice "${item.invoiceNumber}"? Safe links will be detached first and this cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.actionError = null;
    this.deleting = true;
    this.invoicesService.delete(item.id).subscribe({
      next: () => {
        this.toast.success('Invoice deleted.');
        this.router.navigateByUrl('/invoices');
      },
      error: (err) => {
        this.deleting = false;
        this.actionError = err.error?.message || 'Failed to delete invoice.';
        this.toast.error(this.actionError!);
      }
    });
  }

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Finance', path: '/invoices' },
      { label: 'Invoices', path: '/invoices' },
      { label: this.item?.invoiceNumber ?? '…', path: undefined }
    ];
  }
}
