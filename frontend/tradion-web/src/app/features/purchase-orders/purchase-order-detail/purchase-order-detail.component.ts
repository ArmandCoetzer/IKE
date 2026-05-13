import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PurchaseOrdersService, PurchaseOrderDto } from '../../../core/services/purchase-orders.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-purchase-order-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './purchase-order-detail.component.html',
  styleUrl: './purchase-order-detail.component.scss'
})
export class PurchaseOrderDetailComponent implements OnInit {
  item: PurchaseOrderDto | null = null;
  loading = true;
  error: string | null = null;
  actionError: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private purchaseOrdersService: PurchaseOrdersService,
    private documentsService: DocumentsService
  ) {}

  get returnTo(): string | null {
    return sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  goBack(): void {
    const url = this.returnTo || '/purchase-orders';
    this.router.navigateByUrl(url);
  }

  updatingStatus = false;
  statusError: string | null = null;

  downloadPdf(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.documentsService.getPurchaseOrderPdf(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `PO-${this.item?.poNumber ?? id}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: () => (this.actionError = 'Download failed.')
    });
  }

  downloadClientPO(): void {
    const id = this.item?.id;
    if (!id) return;
    this.actionError = null;
    this.documentsService.getPurchaseOrderClientPO(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `ClientPO-${this.item?.poNumber ?? id}`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: () => (this.actionError = 'Download failed.')
    });
  }

  setStatus(status: string): void {
    if (!this.item?.id) return;
    this.statusError = null;
    this.updatingStatus = true;
    this.purchaseOrdersService.updateStatus(this.item.id, status).subscribe({
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

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.purchaseOrdersService.get(id).subscribe({
      next: (po) => {
        this.item = po;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load purchase order.';
        this.loading = false;
      }
    });
  }
}
