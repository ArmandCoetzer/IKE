import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

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
  page = 1;
  readonly pageSize = 10;

  constructor(
    private suppliersService: SuppliersService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.loading = true;
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
}
