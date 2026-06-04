import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ClientsService, ClientDto, ClientImportRowDto } from '../../../core/services/clients.service';
import { AuthService } from '../../../core/services/auth.service';
import { ToastService } from '../../../core/services/toast.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-clients-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './clients-list.component.html',
  styleUrl: './clients-list.component.scss'
})
export class ClientsListComponent implements OnInit {
  clients: ClientDto[] = [];
  searchText = '';
  filterActive: '' | 'true' | 'false' = '';
  loading = true;
  page = 1;
  readonly pageSize = 10;
  importOpen = false;
  importStep: 'select' | 'review' = 'select';
  importFile: File | null = null;
  importRows: ClientImportRowDto[] = [];
  importBusy = false;
  importError: string | null = null;
  editingImportRow: number | null = null;

  constructor(
    private clientsService: ClientsService,
    public auth: AuthService,
    private toast: ToastService
  ) {}

  loadClients(): void {
    this.loading = true;
    const isActive = this.filterActive === 'true' ? true : this.filterActive === 'false' ? false : undefined;
    this.clientsService.list(isActive).subscribe({
      next: (list: ClientDto[]) => {
        this.clients = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadClients();
  }

  get filtered(): ClientDto[] {
    const q = this.searchText?.toLowerCase().trim();
    if (!q) return this.clients;
    return this.clients.filter(c =>
      (c.companyName ?? '').toLowerCase().includes(q) ||
      (c.contactName ?? '').toLowerCase().includes(q) ||
      (c.email ?? '').toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.loadClients();
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
    this.clientsService.downloadImportTemplate().subscribe({
      next: (blob) => this.downloadBlob(blob, 'client-import-template.xlsx'),
      error: () => this.toast.error('Failed to download client import template.')
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
    this.clientsService.importPreview(this.importFile).subscribe({
      next: (result) => {
        this.importBusy = false;
        this.importRows = result.rows;
        this.importStep = 'review';
        this.editingImportRow = null;
      },
      error: (err) => {
        this.importBusy = false;
        this.importError = err.error?.message || 'Failed to preview client import.';
      }
    });
  }

  commitImportRows(): void {
    if (this.importRows.length === 0) return;
    this.importBusy = true;
    this.importError = null;
    this.clientsService.importCommit(this.importRows).subscribe({
      next: (result) => {
        this.importBusy = false;
        if (result.failedCount > 0) {
          this.importRows = result.failedRows;
          this.editingImportRow = null;
          this.toast.info(`${result.successCount} client row${result.successCount === 1 ? '' : 's'} saved. Fix ${result.failedCount} failed row${result.failedCount === 1 ? '' : 's'} and try again.`);
          this.loadClients();
          return;
        }
        this.toast.success(`${result.successCount} client row${result.successCount === 1 ? '' : 's'} imported.`);
        this.importOpen = false;
        this.loadClients();
      },
      error: (err) => {
        this.importBusy = false;
        this.importError = err.error?.message || 'Failed to import clients.';
      }
    });
  }

  startEditImportRow(row: ClientImportRowDto): void {
    this.editingImportRow = row.rowNumber;
  }

  stopEditImportRow(): void {
    this.editingImportRow = null;
  }

  downloadFailedImportRows(): void {
    const headers = ['Row Number', 'Company Name', 'Contact Name', 'Phone', 'Email', 'Site Name', 'Site Address', 'Errors'];
    const rows = this.importRows.map(row => [
      String(row.rowNumber),
      row.companyName,
      row.contactName ?? '',
      row.phone ?? '',
      row.email ?? '',
      row.siteName,
      row.siteAddress ?? '',
      (row.errors ?? []).join('; ')
    ]);
    const csv = [headers, ...rows].map(r => r.map(this.escapeCsv).join(',')).join('\n');
    this.downloadBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), 'failed-client-import-rows.csv');
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
