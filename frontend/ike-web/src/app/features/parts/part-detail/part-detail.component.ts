import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-part-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './part-detail.component.html',
  styleUrl: './part-detail.component.scss'
})
export class PartDetailComponent implements OnInit {
  item: PartDto | null = null;
  loading = true;
  error: string | null = null;
  deleting = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private partsService: PartsService,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  stockRequestQuantity(): number | undefined {
    if (!this.item) return undefined;
    const reorderNeeded = (this.item.reorderLevel ?? 0) - this.guaranteedQuantity();
    const activeJobShortfall = this.activeJobsTaken() - (this.item.quantity ?? 0);
    const needed = Math.max(reorderNeeded, activeJobShortfall, 0);
    return needed > 0 ? needed : undefined;
  }

  guaranteedQuantity(): number {
    if (!this.item) return 0;
    return this.item.availableQuantity ?? this.item.quantity ?? 0;
  }

  activeJobsTaken(): number {
    return this.item?.reservedForActiveJobsQuantity ?? 0;
  }

  canRequestStock(): boolean {
    const p = this.item;
    return !!p && this.hasLinkedSuppliers() && !!p.hasSupplierEmail;
  }

  hasLinkedSuppliers(): boolean {
    const p = this.item;
    return !!p && (!!p.supplierId || (p.supplierIds?.length ?? 0) > 0);
  }

  needsPartSetupFix(): boolean {
    const p = this.item;
    return !!p && !p.isLabour && (!this.hasLinkedSuppliers() || !p.hasSupplierEmail || p.reorderLevel <= 0 || !p.unit?.trim());
  }

  canShowRequestStock(): boolean {
    const p = this.item;
    return !!p && p.isLowStock && !p.isLabour && this.canRequestStock() && !this.needsPartSetupFix();
  }

  canRestockFromLinkedSuppliers(): boolean {
    const p = this.item;
    return !!p && !p.isLabour && this.hasLinkedSuppliers();
  }

  linkedSupplierNames(): string[] {
    const p = this.item;
    if (!p || p.isLabour) return [];
    return p.supplierNames?.length ? p.supplierNames : (p.supplierName ? [p.supplierName] : []);
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.partsService.get(id).subscribe({
      next: (p) => {
        this.item = p;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load part.';
        this.loading = false;
      }
    });
  }

  async deletePart(): Promise<void> {
    const item = this.item;
    if (!item?.id || this.deleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete part',
      message: `Delete part "${item.name}"? This will remove safe links first and cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.deleting = true;
    this.partsService.delete(item.id).subscribe({
      next: () => {
        this.toast.success('Part deleted.');
        this.router.navigateByUrl('/parts');
      },
      error: (err) => {
        this.deleting = false;
        this.toast.error(err.error?.message || 'Failed to delete part.');
      }
    });
  }
}
