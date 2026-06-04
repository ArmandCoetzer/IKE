import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, map, of } from 'rxjs';
import { InvoicesService, InvoiceDto } from '../../../core/services/invoices.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-invoices-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './invoices-list.component.html',
  styleUrl: './invoices-list.component.scss'
})
export class InvoicesListComponent implements OnInit {
  items: InvoiceDto[] = [];
  clients: ClientDto[] = [];
  sites: SiteDto[] = [];
  searchText = '';
  filterStatus = '';
  filterClientId = '';
  filterSiteId = '';
  loading = true;
  deletingInvoiceId: string | null = null;
  selectedInvoiceIds = new Set<string>();
  bulkDeleting = false;
  bulkDeleteFailures: string[] = [];
  page = 1;
  readonly pageSize = 10;

  constructor(
    private invoicesService: InvoicesService,
    private clientsService: ClientsService,
    private sitesService: SitesService,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  get filtered(): InvoiceDto[] {
    const q = this.searchText?.toLowerCase().trim();
    if (!q) return this.items;
    return this.items.filter(i =>
      (i.invoiceNumber ?? '').toLowerCase().includes(q) ||
      (i.clientName ?? '').toLowerCase().includes(q)
    );
  }

  loadInvoices(): void {
    this.loading = true;
    this.selectedInvoiceIds.clear();
    this.bulkDeleteFailures = [];
    const clientId = this.filterClientId?.trim() || undefined;
    const siteId = this.filterSiteId?.trim() || undefined;
    const status = this.filterStatus?.trim() || undefined;
    this.invoicesService.list(clientId, siteId, status).subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  onClientFilterChange(): void {
    this.page = 1;
    this.filterSiteId = '';
    this.loadSites();
    this.loadInvoices();
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadInvoices();
  }

  loadSites(): void {
    const clientId = this.filterClientId?.trim() || undefined;
    this.sitesService.list(clientId, true).subscribe({
      next: (list) => (this.sites = list),
      error: () => (this.sites = [])
    });
  }

  isOverdue(inv: InvoiceDto): boolean {
    if (inv.status === 'Paid') return false;
    if (!inv.dueDate) return false;
    const due = new Date(inv.dueDate);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    due.setHours(0, 0, 0, 0);
    return due < today;
  }

  get pagedFiltered(): InvoiceDto[] {
    return this.filtered.slice((this.page - 1) * this.pageSize, this.page * this.pageSize);
  }

  get allSelected(): boolean {
    return this.filtered.length > 0 && this.filtered.every(i => this.selectedInvoiceIds.has(i.id));
  }

  get someSelected(): boolean {
    return this.selectedInvoiceIds.size > 0;
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) this.filtered.forEach(i => this.selectedInvoiceIds.add(i.id));
    else this.filtered.forEach(i => this.selectedInvoiceIds.delete(i.id));
  }

  toggleSelect(id: string, checked: boolean): void {
    if (checked) this.selectedInvoiceIds.add(id);
    else this.selectedInvoiceIds.delete(id);
  }

  isSelected(id: string): boolean {
    return this.selectedInvoiceIds.has(id);
  }

  async deleteInvoice(inv: InvoiceDto): Promise<void> {
    if (!inv.id || this.deletingInvoiceId) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete invoice',
      message: `Delete invoice "${inv.invoiceNumber}"? Safe links will be detached first and this cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.deletingInvoiceId = inv.id;
    this.invoicesService.delete(inv.id).subscribe({
      next: () => {
        this.items = this.items.filter(item => item.id !== inv.id);
        this.selectedInvoiceIds.delete(inv.id);
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        this.deletingInvoiceId = null;
        this.bulkDeleteFailures = [];
        this.toast.success('Invoice deleted.');
      },
      error: (err) => {
        this.deletingInvoiceId = null;
        this.toast.error(err.error?.message || 'Failed to delete invoice.');
      }
    });
  }

  async deleteSelectedInvoices(): Promise<void> {
    const selected = this.items.filter(i => this.selectedInvoiceIds.has(i.id));
    if (selected.length === 0 || this.bulkDeleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete selected invoices',
      message: `Delete ${selected.length} selected invoice${selected.length === 1 ? '' : 's'}? Sent, waiting-payment, paid, or otherwise locked invoices will be skipped with an explanation.`,
      confirmText: 'Delete selected',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;

    this.bulkDeleting = true;
    this.bulkDeleteFailures = [];
    forkJoin(selected.map(invoice =>
      this.invoicesService.delete(invoice.id).pipe(
        map(() => ({ id: invoice.id, label: invoice.invoiceNumber, success: true, message: '' })),
        catchError(err => of({
          id: invoice.id,
          label: invoice.invoiceNumber,
          success: false,
          message: err.error?.message || 'Delete was blocked.'
        }))
      )
    )).subscribe({
      next: (results) => {
        const deletedIds = new Set(results.filter(r => r.success).map(r => r.id));
        this.items = this.items.filter(i => !deletedIds.has(i.id));
        deletedIds.forEach(id => this.selectedInvoiceIds.delete(id));
        this.bulkDeleteFailures = results
          .filter(r => !r.success)
          .map(r => `${r.label}: ${r.message}`);
        this.bulkDeleting = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        if (this.bulkDeleteFailures.length === 0) {
          this.selectedInvoiceIds.clear();
          this.toast.success(`${deletedIds.size} invoice${deletedIds.size === 1 ? '' : 's'} deleted.`);
        } else if (deletedIds.size > 0) {
          this.toast.info(`${deletedIds.size} deleted. ${this.bulkDeleteFailures.length} could not be deleted.`);
        } else {
          this.toast.error('No selected invoices could be deleted.');
        }
      },
      error: () => {
        this.bulkDeleting = false;
        this.toast.error('Failed to delete selected invoices.');
      }
    });
  }

  ngOnInit(): void {
    this.clientsService.list(true).subscribe({
      next: (clients) => (this.clients = clients),
      error: () => {}
    });
    this.loadSites();
    this.loadInvoices();
  }
}
