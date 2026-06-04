import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

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

  constructor(
    private partsService: PartsService,
    public auth: AuthService
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

  stockRequestQuantity(p: PartDto): number | undefined {
    const needed = (p.reorderLevel ?? 0) - this.guaranteedQuantity(p);
    return needed > 0 ? needed : undefined;
  }

  guaranteedQuantity(p: PartDto): number {
    return p.availableQuantity ?? p.quantity ?? 0;
  }

  activeJobsTaken(p: PartDto): number {
    return p.reservedForActiveJobsQuantity ?? 0;
  }

  canRequestStock(p: PartDto): boolean {
    return !!p.supplierId && !!p.hasSupplierEmail;
  }

  ngOnInit(): void {
    this.loadParts();
  }
}
