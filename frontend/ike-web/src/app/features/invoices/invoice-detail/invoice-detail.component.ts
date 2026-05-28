import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { InvoicesService, InvoiceDto } from '../../../core/services/invoices.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { AuthService } from '../../../core/services/auth.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';

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
    public auth: AuthService
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

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Finance', path: '/invoices' },
      { label: 'Invoices', path: '/invoices' },
      { label: this.item?.invoiceNumber ?? '…', path: undefined }
    ];
  }
}
