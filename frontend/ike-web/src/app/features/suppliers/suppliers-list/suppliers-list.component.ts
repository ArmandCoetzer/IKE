import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, map, of } from 'rxjs';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-suppliers-list',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './suppliers-list.component.html'
})
export class SuppliersListComponent implements OnInit {
  suppliers: SupplierDto[] = [];
  loading = true;
  error: string | null = null;
  deletingSupplierId: string | null = null;
  selectedSupplierIds = new Set<string>();
  bulkDeleting = false;
  bulkDeleteFailures: string[] = [];
  page = 1;
  readonly pageSize = 10;

  constructor(
    private suppliersService: SuppliersService,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.loading = true;
    this.selectedSupplierIds.clear();
    this.bulkDeleteFailures = [];
    this.suppliersService.list().subscribe({
      next: (list) => {
        this.suppliers = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.suppliers.length, this.pageSize);
      },
      error: () => {
        this.error = 'Failed to load suppliers.';
        this.loading = false;
      }
    });
  }

  hasLinkedParts(supplier: SupplierDto): boolean {
    return (supplier.partIds?.length ?? 0) > 0;
  }

  get pagedSuppliers(): SupplierDto[] {
    return this.suppliers.slice((this.page - 1) * this.pageSize, this.page * this.pageSize);
  }

  get allSelected(): boolean {
    return this.suppliers.length > 0 && this.suppliers.every(s => this.selectedSupplierIds.has(s.id));
  }

  get someSelected(): boolean {
    return this.selectedSupplierIds.size > 0;
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) this.suppliers.forEach(s => this.selectedSupplierIds.add(s.id));
    else this.suppliers.forEach(s => this.selectedSupplierIds.delete(s.id));
  }

  toggleSelect(id: string, checked: boolean): void {
    if (checked) this.selectedSupplierIds.add(id);
    else this.selectedSupplierIds.delete(id);
  }

  isSelected(id: string): boolean {
    return this.selectedSupplierIds.has(id);
  }

  async deleteSupplier(supplier: SupplierDto): Promise<void> {
    if (!supplier.id || this.deletingSupplierId) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete supplier',
      message: `Delete supplier "${supplier.name}"? Safe part links and draft requests will be removed first.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.deletingSupplierId = supplier.id;
    this.suppliersService.delete(supplier.id).subscribe({
      next: () => {
        this.suppliers = this.suppliers.filter(item => item.id !== supplier.id);
        this.selectedSupplierIds.delete(supplier.id);
        this.page = clampTablePage(this.page, this.suppliers.length, this.pageSize);
        this.deletingSupplierId = null;
        this.bulkDeleteFailures = [];
        this.toast.success('Supplier deleted.');
      },
      error: (err) => {
        this.deletingSupplierId = null;
        this.toast.error(err.error?.message || 'Failed to delete supplier.');
      }
    });
  }

  async deleteSelectedSuppliers(): Promise<void> {
    const selected = this.suppliers.filter(s => this.selectedSupplierIds.has(s.id));
    if (selected.length === 0 || this.bulkDeleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete selected suppliers',
      message: `Delete ${selected.length} selected supplier${selected.length === 1 ? '' : 's'}? Suppliers with quoted or ordered requests will be skipped with an explanation.`,
      confirmText: 'Delete selected',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;

    this.bulkDeleting = true;
    this.bulkDeleteFailures = [];
    forkJoin(selected.map(supplier =>
      this.suppliersService.delete(supplier.id).pipe(
        map(() => ({ id: supplier.id, label: supplier.name, success: true, message: '' })),
        catchError(err => of({
          id: supplier.id,
          label: supplier.name,
          success: false,
          message: err.error?.message || 'Delete was blocked.'
        }))
      )
    )).subscribe({
      next: (results) => {
        const deletedIds = new Set(results.filter(r => r.success).map(r => r.id));
        this.suppliers = this.suppliers.filter(s => !deletedIds.has(s.id));
        deletedIds.forEach(id => this.selectedSupplierIds.delete(id));
        this.bulkDeleteFailures = results
          .filter(r => !r.success)
          .map(r => `${r.label}: ${r.message}`);
        this.bulkDeleting = false;
        this.page = clampTablePage(this.page, this.suppliers.length, this.pageSize);
        if (this.bulkDeleteFailures.length === 0) {
          this.selectedSupplierIds.clear();
          this.toast.success(`${deletedIds.size} supplier${deletedIds.size === 1 ? '' : 's'} deleted.`);
        } else if (deletedIds.size > 0) {
          this.toast.info(`${deletedIds.size} deleted. ${this.bulkDeleteFailures.length} could not be deleted.`);
        } else {
          this.toast.error('No selected suppliers could be deleted.');
        }
      },
      error: () => {
        this.bulkDeleting = false;
        this.toast.error('Failed to delete selected suppliers.');
      }
    });
  }
}
