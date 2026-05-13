import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuditErrorEntryDto, AuditErrorsService } from '../../../core/services/audit-errors.service';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-audit-errors',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './audit-errors.component.html'
})
export class AuditErrorsComponent implements OnInit {
  loading = true;
  error: string | null = null;
  rows: AuditErrorEntryDto[] = [];
  page = 1;
  readonly pageSize = 10;

  constructor(
    private auditErrorsService: AuditErrorsService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.error = null;
    this.auditErrorsService.list(500, 0).subscribe({
      next: (rows) => {
        this.rows = rows;
        this.loading = false;
        this.page = clampTablePage(this.page, this.rows.length, this.pageSize);
      },
      error: (err) => {
        this.error = err?.error?.message || 'Failed to load audit errors.';
        this.loading = false;
      }
    });
  }
}
