import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { AuthService } from '../../../core/services/auth.service';
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

  constructor(
    private clientsService: ClientsService,
    public auth: AuthService
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
}
