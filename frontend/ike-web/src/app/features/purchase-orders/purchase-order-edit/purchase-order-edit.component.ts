import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PurchaseOrdersService, PurchaseOrderDto, UpdatePurchaseOrderRequest } from '../../../core/services/purchase-orders.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ToastService } from '../../../core/services/toast.service';

@Component({
  selector: 'app-purchase-order-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './purchase-order-edit.component.html',
  styleUrl: './purchase-order-edit.component.scss'
})
export class PurchaseOrderEditComponent implements OnInit {
  id: string | null = null;
  item: PurchaseOrderDto | null = null;
  clientPONumber = '';
  amount = 0;
  currency = 'ZAR';
  notes = '';
  loading = false;
  submitting = false;
  error: string | null = null;
  selectedClientPOFile: File | null = null;
  uploadingClientPO = false;
  uploadError: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private purchaseOrdersService: PurchaseOrdersService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.loading = true;
    this.purchaseOrdersService.get(this.id).subscribe({
      next: (po) => {
        this.applyPurchaseOrder(po);
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load purchase order.';
        this.loading = false;
      }
    });
  }

  private applyPurchaseOrder(po: PurchaseOrderDto): void {
    this.item = po;
    this.clientPONumber = po.clientPONumber ?? '';
    this.amount = po.amount ?? 0;
    this.currency = po.currency ?? 'ZAR';
    this.notes = po.notes ?? '';
  }

  onClientPOFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.selectedClientPOFile = file ?? null;
    this.uploadError = null;
  }

  uploadClientPO(): void {
    if (!this.id || !this.selectedClientPOFile) return;
    this.uploadError = null;
    this.uploadingClientPO = true;
    this.purchaseOrdersService.uploadClientPO(this.id, this.selectedClientPOFile).subscribe({
      next: () => {
        this.uploadingClientPO = false;
        this.selectedClientPOFile = null;
        this.purchaseOrdersService.get(this.id!).subscribe({
          next: (po) => {
            if (this.item) this.item = po;
          }
        });
      },
      error: (err) => {
        this.uploadingClientPO = false;
        this.uploadError = err.error?.message ?? err.message ?? 'Upload failed.';
      }
    });
  }

  save(): void {
    if (!this.id) return;
    this.error = null;
    this.submitting = true;
    const body: UpdatePurchaseOrderRequest = {
      clientPONumber: this.clientPONumber.trim() || undefined,
      amount: this.amount,
      currency: this.currency,
      notes: this.notes.trim() || undefined
    };
    this.purchaseOrdersService.update(this.id, body).subscribe({
      next: (purchaseOrder) => {
        this.submitting = false;
        this.applyPurchaseOrder(purchaseOrder);
        this.toast.success('Purchase order updated.');
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update purchase order.';
        this.toast.error(this.error!);
      }
    });
  }
}
