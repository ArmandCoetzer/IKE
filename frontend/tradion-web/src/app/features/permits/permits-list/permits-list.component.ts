import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PermitTypesService, PermitTypeDto } from '../../../core/services/permit-types.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ActivationFilterStatus, ActivationFilterStatusType } from '../../../core/status/activation-status';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-permits-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './permits-list.component.html',
  styleUrl: './permits-list.component.scss'
})
export class PermitsListComponent implements OnInit {
  items: PermitTypeDto[] = [];
  loading = true;
  readonly activationFilter = ActivationFilterStatus;
  filterStatus: ActivationFilterStatusType = ActivationFilterStatus.all;
  page = 1;
  readonly pageSize = 10;

  constructor(private permitTypesService: PermitTypesService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    const isActive = this.filterStatus === ActivationFilterStatus.all
      ? undefined
      : this.filterStatus === ActivationFilterStatus.active;
    this.permitTypesService.list(isActive).subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.items.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  onFilterChange(): void {
    this.page = 1;
    this.load();
  }
}
