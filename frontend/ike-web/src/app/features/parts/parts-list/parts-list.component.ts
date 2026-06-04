import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, map, of } from 'rxjs';
import { PartsService, PartDto, PartImportRowDto } from '../../../core/services/parts.service';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-parts-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './parts-list.component.html',
  styleUrl: './parts-list.component.scss'
})
export class PartsListComponent implements OnInit {
  parts: PartDto[] = [];
  loading = true;
  searchText = '';
  lowStockOnly = false;
  page = 1;
  readonly pageSize = 10;
  importOpen = false;
  importStep: 'select' | 'review' = 'select';
  importFile: File | null = null;
  importRows: PartImportRowDto[] = [];
  importBusy = false;
  importError: string | null = null;
  editingImportRow: number | null = null;
  deletingPartId: string | null = null;
  selectedPartIds = new Set<string>();
  bulkDeleting = false;
  bulkDeleteFailures: string[] = [];
  restockPart: PartDto | null = null;
  restockQuantity = 0;
  restockSaving = false;
  restockError: string | null = null;

  constructor(
    private partsService: PartsService,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  get filtered(): PartDto[] {
    const q = this.searchText?.toLowerCase().trim();
    if (!q) return this.parts;
    return this.parts.filter(p =>
      (p.name ?? '').toLowerCase().includes(q) ||
      (p.partNumber ?? '').toLowerCase().includes(q)
    );
  }

  loadParts(): void {
    this.loading = true;
    this.partsService.list(this.lowStockOnly).subscribe({
      next: (list: PartDto[]) => {
        this.parts = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadParts();
  }

  get pagedFiltered(): PartDto[] {
    return this.filtered.slice((this.page - 1) * this.pageSize, this.page * this.pageSize);
  }

  get allSelected(): boolean {
    return this.filtered.length > 0 && this.filtered.every(p => this.selectedPartIds.has(p.id));
  }

  get someSelected(): boolean {
    return this.selectedPartIds.size > 0;
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) this.filtered.forEach(p => this.selectedPartIds.add(p.id));
    else this.filtered.forEach(p => this.selectedPartIds.delete(p.id));
  }

  toggleSelect(id: string, checked: boolean): void {
    if (checked) this.selectedPartIds.add(id);
    else this.selectedPartIds.delete(id);
  }

  isSelected(id: string): boolean {
    return this.selectedPartIds.has(id);
  }

  async deletePart(p: PartDto): Promise<void> {
    if (!p.id || this.deletingPartId) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete part',
      message: `Delete part "${p.name}"? This will remove safe links first and cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.deletingPartId = p.id;
    this.partsService.delete(p.id).subscribe({
      next: () => {
        this.parts = this.parts.filter(item => item.id !== p.id);
        this.selectedPartIds.delete(p.id);
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        this.deletingPartId = null;
        this.bulkDeleteFailures = [];
        this.toast.success('Part deleted.');
      },
      error: (err) => {
        this.deletingPartId = null;
        this.toast.error(err.error?.message || 'Failed to delete part.');
      }
    });
  }

  async deleteSelectedParts(): Promise<void> {
    const selected = this.parts.filter(p => this.selectedPartIds.has(p.id));
    if (selected.length === 0 || this.bulkDeleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete selected parts',
      message: `Delete ${selected.length} selected part${selected.length === 1 ? '' : 's'}? Items that are linked to locked records will be skipped with an explanation.`,
      confirmText: 'Delete selected',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;

    this.bulkDeleting = true;
    this.bulkDeleteFailures = [];
    forkJoin(selected.map(part =>
      this.partsService.delete(part.id).pipe(
        map(() => ({ id: part.id, label: part.name, success: true, message: '' })),
        catchError(err => of({
          id: part.id,
          label: part.name,
          success: false,
          message: err.error?.message || 'Delete was blocked.'
        }))
      )
    )).subscribe({
      next: (results) => {
        const deletedIds = new Set(results.filter(r => r.success).map(r => r.id));
        this.parts = this.parts.filter(p => !deletedIds.has(p.id));
        deletedIds.forEach(id => this.selectedPartIds.delete(id));
        this.bulkDeleteFailures = results
          .filter(r => !r.success)
          .map(r => `${r.label}: ${r.message}`);
        this.bulkDeleting = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        if (this.bulkDeleteFailures.length === 0) {
          this.selectedPartIds.clear();
          this.toast.success(`${deletedIds.size} part${deletedIds.size === 1 ? '' : 's'} deleted.`);
        } else if (deletedIds.size > 0) {
          this.toast.info(`${deletedIds.size} deleted. ${this.bulkDeleteFailures.length} could not be deleted.`);
        } else {
          this.toast.error('No selected parts could be deleted.');
        }
      },
      error: () => {
        this.bulkDeleting = false;
        this.toast.error('Failed to delete selected parts.');
      }
    });
  }

  guaranteedQuantity(p: PartDto): number {
    return p.availableQuantity ?? p.quantity ?? 0;
  }

  activeJobsTaken(p: PartDto): number {
    return p.reservedForActiveJobsQuantity ?? 0;
  }

  partIssueMessages(p: PartDto): string[] {
    if (p.isLabour) return [];
    const issues: string[] = [];
    if (!this.hasLinkedSupplier(p) || !p.hasSupplierEmail) issues.push('Supplier missing');
    if ((p.reorderLevel ?? 0) <= 0) issues.push('Reorder level missing');
    if (!p.unit?.trim()) issues.push('Unit of measurement missing');
    if ((p.quantity ?? 0) <= 0) issues.push('Stock is 0');
    if (this.activeJobsTaken(p) > (p.quantity ?? 0)) issues.push('Active jobs need more stock than is available');
    return issues;
  }

  partIssueTitle(p: PartDto): string {
    return this.partIssueMessages(p).join('. ');
  }

  hasPartSetupIssue(p: PartDto): boolean {
    return this.partIssueMessages(p).length > 0;
  }

  needsPartSetupFix(p: PartDto): boolean {
    return !p.isLabour && (!this.hasLinkedSupplier(p) || !p.hasSupplierEmail || p.reorderLevel <= 0 || !p.unit?.trim());
  }

  private hasLinkedSupplier(p: PartDto): boolean {
    return !!p.supplierId || (p.supplierIds?.length ?? 0) > 0;
  }

  openRestockDialog(p: PartDto): void {
    this.restockPart = p;
    this.restockQuantity = Math.max(0, Number(p.reorderLevel) || 0);
    this.restockError = null;
  }

  closeRestockDialog(): void {
    if (this.restockSaving) return;
    this.restockPart = null;
    this.restockError = null;
  }

  saveRestockQuantity(): void {
    const part = this.restockPart;
    if (!part || this.restockSaving) return;
    const quantity = Math.max(0, Math.round(Number(this.restockQuantity) || 0));
    this.restockQuantity = quantity;
    this.restockSaving = true;
    this.restockError = null;
    this.partsService.update(part.id, { quantity }).subscribe({
      next: (updated) => {
        this.parts = this.parts.map(p => p.id === updated.id ? updated : p);
        this.restockSaving = false;
        this.restockPart = null;
        this.toast.success('Part quantity updated.');
      },
      error: (err) => {
        this.restockSaving = false;
        this.restockError = err.error?.message || 'Failed to update part quantity.';
      }
    });
  }

  ngOnInit(): void {
    this.loadParts();
  }

  openImportDialog(): void {
    this.importOpen = true;
    this.importStep = 'select';
    this.importFile = null;
    this.importRows = [];
    this.importError = null;
    this.editingImportRow = null;
  }

  closeImportDialog(): void {
    if (this.importBusy) return;
    this.importOpen = false;
  }

  downloadImportTemplate(): void {
    this.partsService.downloadImportTemplate().subscribe({
      next: (blob) => this.downloadBlob(blob, 'part-import-template.xlsx'),
      error: () => this.toast.error('Failed to download part import template.')
    });
  }

  onImportFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.importFile = input.files?.[0] ?? null;
    this.importRows = [];
    this.importError = null;
    if (this.importFile) {
      this.previewImportFile();
    }
  }

  previewImportFile(): void {
    if (!this.importFile) {
      this.importError = 'Select an XLSX file first.';
      return;
    }
    this.importBusy = true;
    this.partsService.importPreview(this.importFile).subscribe({
      next: (result) => {
        this.importBusy = false;
        this.importRows = result.rows;
        this.importStep = 'review';
        this.editingImportRow = null;
      },
      error: (err) => {
        this.importBusy = false;
        this.importError = err.error?.message || 'Failed to preview part import.';
      }
    });
  }

  commitImportRows(): void {
    if (this.importRows.length === 0) return;
    this.importBusy = true;
    this.importError = null;
    this.partsService.importCommit(this.importRows).subscribe({
      next: (result) => {
        this.importBusy = false;
        if (result.failedCount > 0) {
          this.importRows = result.failedRows;
          this.editingImportRow = null;
          this.toast.info(`${result.successCount} part row${result.successCount === 1 ? '' : 's'} saved. Fix ${result.failedCount} failed row${result.failedCount === 1 ? '' : 's'} and try again.`);
          this.loadParts();
          return;
        }
        this.toast.success(`${result.successCount} part row${result.successCount === 1 ? '' : 's'} imported.`);
        this.importOpen = false;
        this.loadParts();
      },
      error: (err) => {
        this.importBusy = false;
        this.importError = err.error?.message || 'Failed to import parts.';
      }
    });
  }

  startEditImportRow(row: PartImportRowDto): void {
    this.editingImportRow = row.rowNumber;
  }

  stopEditImportRow(): void {
    this.editingImportRow = null;
  }

  downloadFailedImportRows(): void {
    const headers = ['Row Number', 'Name', 'Description', 'Part Number', 'Quantity', 'Reorder Level', 'Unit', 'Unit Price', 'Is Labour', 'Errors'];
    const rows = this.importRows.map(row => [
      String(row.rowNumber),
      row.name,
      row.description ?? '',
      row.partNumber ?? '',
      String(row.quantity),
      String(row.reorderLevel),
      row.unit ?? '',
      String(row.unitPrice),
      String(row.isLabour),
      (row.errors ?? []).join('; ')
    ]);
    const csv = [headers, ...rows].map(r => r.map(this.escapeCsv).join(',')).join('\n');
    this.downloadBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), 'failed-part-import-rows.csv');
  }

  private downloadBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  private escapeCsv(value: string): string {
    return /[",\n\r]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
  }
}
