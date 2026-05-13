import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PurchaseOrdersService, PurchaseOrderDto } from '../../../core/services/purchase-orders.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-purchase-orders-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './purchase-orders-list.component.html',
  styleUrl: './purchase-orders-list.component.scss'
})
export class PurchaseOrdersListComponent implements OnInit {
  items: PurchaseOrderDto[] = [];
  clients: ClientDto[] = [];
  searchText = '';
  filterClientId = '';
  loading = true;
  page = 1;
  readonly pageSize = 10;

  constructor(
    private purchaseOrdersService: PurchaseOrdersService,
    private clientsService: ClientsService,
    public auth: AuthService
  ) {}

  get filtered(): PurchaseOrderDto[] {
    const q = this.searchText?.toLowerCase().trim();
    const clientId = this.filterClientId?.trim();
    let list = this.items;
    if (clientId) list = list.filter(po => po.clientId === clientId);
    if (!q) return list;
    return list.filter(po =>
      (po.poNumber ?? '').toLowerCase().includes(q) ||
      (po.clientPONumber ?? '').toLowerCase().includes(q) ||
      (po.clientName ?? '').toLowerCase().includes(q) ||
      (po.siteName ?? '').toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.clientsService.list(true).subscribe({ next: (list) => (this.clients = list) });
    this.purchaseOrdersService.list().subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }
}
