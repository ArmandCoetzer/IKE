import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ServiceRequestsService, ServiceRequestDto } from '../../../core/services/service-requests.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-service-requests-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './service-requests-list.component.html',
  styleUrl: './service-requests-list.component.scss'
})
export class ServiceRequestsListComponent implements OnInit {
  items: ServiceRequestDto[] = [];
  searchText = '';
  filterStatus = '';
  loading = true;
  page = 1;
  readonly pageSize = 10;

  constructor(
    private serviceRequestsService: ServiceRequestsService,
    public auth: AuthService
  ) {}

  get filtered(): ServiceRequestDto[] {
    const q = this.searchText?.toLowerCase().trim();
    const status = this.filterStatus?.trim().toLowerCase();
    let list = this.items;
    if (status) list = list.filter(r => (r.status ?? '').toLowerCase() === status);
    if (!q) return list;
    return list.filter(r =>
      (r.requestNumber ?? '').toLowerCase().includes(q) ||
      (r.siteName ?? '').toLowerCase().includes(q) ||
      (r.description ?? '').toLowerCase().includes(q) ||
      String(r.priority ?? '').toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.serviceRequestsService.list().subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }
}
