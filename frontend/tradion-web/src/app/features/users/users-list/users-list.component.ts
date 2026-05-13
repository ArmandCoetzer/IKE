import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { UsersService, UserListDto } from '../../../core/services/users.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ActivationFilterStatus, ActivationFilterStatusType } from '../../../core/status/activation-status';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-users-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './users-list.component.html',
  styleUrl: './users-list.component.scss'
})
export class UsersListComponent implements OnInit {
  users: UserListDto[] = [];
  filtered: UserListDto[] = [];
  loading = true;
  filterRole: string | null = null;
  readonly activationFilter = ActivationFilterStatus;
  filterStatus: ActivationFilterStatusType = ActivationFilterStatus.all;
  searchText = '';
  roles: string[] = [];
  selectedIds = new Set<string>();
  batchInProgress = false;
  page = 1;
  readonly pageSize = 10;

  constructor(private usersService: UsersService) {}

  ngOnInit(): void {
    this.usersService.getRoles(true).subscribe({ next: (list) => (this.roles = list) });
    this.load();
  }

  load(): void {
    this.loading = true;
    this.selectedIds.clear();
    const isActive = this.filterStatus === ActivationFilterStatus.all
      ? undefined
      : this.filterStatus === ActivationFilterStatus.active;
    this.usersService.list(this.filterRole ?? undefined, isActive).subscribe({
      next: (list) => {
        this.users = list.filter((u) => (u.role || '') !== 'Client');
        this.applySearch();
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  applySearch(): void {
    const q = this.searchText.trim().toLowerCase();
    if (!q) {
      this.filtered = this.users;
    } else {
      this.filtered = this.users.filter(
        (u) =>
          (u.email && u.email.toLowerCase().includes(q)) ||
          (u.fullName && u.fullName.toLowerCase().includes(q)) ||
          (u.firstName && u.firstName.toLowerCase().includes(q)) ||
          (u.lastName && u.lastName.toLowerCase().includes(q))
      );
    }
    this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
  }

  get pagedFiltered(): UserListDto[] {
    const list = this.filtered;
    const start = (this.page - 1) * this.pageSize;
    return list.slice(start, start + this.pageSize);
  }

  onFilterChange(): void {
    this.page = 1;
    this.load();
  }

  onSearch(): void {
    this.page = 1;
    this.applySearch();
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) {
      this.pagedFiltered.forEach((u) => this.selectedIds.add(u.id));
    } else {
      this.pagedFiltered.forEach((u) => this.selectedIds.delete(u.id));
    }
  }

  toggleSelect(id: string, checked: boolean): void {
    if (checked) this.selectedIds.add(id);
    else this.selectedIds.delete(id);
  }

  isSelected(id: string): boolean {
    return this.selectedIds.has(id);
  }

  get allSelected(): boolean {
    return this.pagedFiltered.length > 0 && this.pagedFiltered.every((u) => this.selectedIds.has(u.id));
  }

  get someSelected(): boolean {
    return this.selectedIds.size > 0;
  }

  batchSetActive(active: boolean): void {
    const ids = Array.from(this.selectedIds);
    if (ids.length === 0) return;
    this.batchInProgress = true;
    this.usersService.batchSetStatus(ids, active).subscribe({
      next: () => {
        this.batchInProgress = false;
        this.selectedIds.clear();
        this.load();
      },
      error: () => (this.batchInProgress = false)
    });
  }

  exportCsv(): void {
    const rows = this.filtered;
    const headers = ['Name', 'Email', 'Role', 'Company', 'Status', 'Registration'];
    const csvRows = [
      headers.join(','),
      ...rows.map((u) =>
        [
          `"${(u.fullName || u.email || '').replace(/"/g, '""')}"`,
          `"${(u.email || '').replace(/"/g, '""')}"`,
          `"${(u.role || '').replace(/"/g, '""')}"`,
          `"${(u.companyName || '').replace(/"/g, '""')}"`,
          u.isActive ? 'Active' : 'Inactive',
          u.registrationStatus || ''
        ].join(',')
      )
    ];
    const blob = new Blob([csvRows.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `employees-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 60000);
  }
}
