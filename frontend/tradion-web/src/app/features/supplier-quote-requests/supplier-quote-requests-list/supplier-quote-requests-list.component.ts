import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SupplierQuoteRequestsService, SupplierQuoteRequestDto } from '../../../core/services/supplier-quote-requests.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-supplier-quote-requests-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './supplier-quote-requests-list.component.html'
})
export class SupplierQuoteRequestsListComponent implements OnInit {
  items: SupplierQuoteRequestDto[] = [];
  loading = true;
  statusFilter = '';
  updating: string | null = null;
  page = 1;
  readonly pageSize = 10;

  constructor(private service: SupplierQuoteRequestsService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.service.list(this.statusFilter || undefined).subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.items.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  setStatus(item: SupplierQuoteRequestDto, status: string): void {
    if (this.updating) return;
    this.updating = item.id;
    this.service.updateStatus(item.id, status).subscribe({
      next: () => {
        item.status = status;
        this.updating = null;
      },
      error: () => (this.updating = null)
    });
  }
}
