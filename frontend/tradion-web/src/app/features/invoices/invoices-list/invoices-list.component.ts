import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InvoicesService, InvoiceDto } from '../../../core/services/invoices.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

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
  page = 1;
  readonly pageSize = 10;

  constructor(
    private invoicesService: InvoicesService,
    private clientsService: ClientsService,
    private sitesService: SitesService,
    public auth: AuthService
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

  ngOnInit(): void {
    this.clientsService.list(true).subscribe({
      next: (clients) => (this.clients = clients),
      error: () => {}
    });
    this.loadSites();
    this.loadInvoices();
  }
}
