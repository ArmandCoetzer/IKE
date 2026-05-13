import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ClientsService, ClientDto, ClientPortalUserDto } from '../../../core/services/clients.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { QuotesService, QuoteDto } from '../../../core/services/quotes.service';
import { PurchaseOrdersService, PurchaseOrderDto } from '../../../core/services/purchase-orders.service';
import { InvoicesService, InvoiceDto } from '../../../core/services/invoices.service';
import { AuthService } from '../../../core/services/auth.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-client-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, BreadcrumbComponent, PageHeaderComponent],
  templateUrl: './client-detail.component.html',
  styleUrl: './client-detail.component.scss'
})
export class ClientDetailComponent implements OnInit {
  item: ClientDto | null = null;
  sites: SiteDto[] = [];
  portalUsers: ClientPortalUserDto[] = [];
  quotes: QuoteDto[] = [];
  purchaseOrders: PurchaseOrderDto[] = [];
  invoices: InvoiceDto[] = [];
  loading = true;
  error: string | null = null;
  portalBusyUserId: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private clientsService: ClientsService,
    private sitesService: SitesService,
    private quotesService: QuotesService,
    private purchaseOrdersService: PurchaseOrdersService,
    private invoicesService: InvoicesService,
    public auth: AuthService
  ) {}

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Resources', path: '/clients' },
      { label: 'Clients', path: '/clients' },
      { label: this.item?.companyName ?? '…' }
    ];
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.clientsService.get(id).subscribe({
      next: (c) => {
        this.item = c;
        this.loading = false;
        if (c?.id) {
          this.sitesService.list(c.id).subscribe({ next: (list) => (this.sites = list) });
          this.loadPortalUsers(c.id);
          this.quotesService.list(c.id).subscribe({ next: (list) => (this.quotes = list) });
          this.purchaseOrdersService.list(c.id).subscribe({ next: (list) => (this.purchaseOrders = list) });
          this.invoicesService.list(c.id).subscribe({ next: (list) => (this.invoices = list) });
        }
      },
      error: () => {
        this.error = 'Failed to load client.';
        this.loading = false;
      }
    });
  }

  loadPortalUsers(clientId: string): void {
    this.clientsService.getPortalUsers(clientId).subscribe({
      next: (list) => (this.portalUsers = list),
      error: () => (this.portalUsers = [])
    });
  }

  reInvitePortalUser(userId: string): void {
    if (!this.item?.id || this.portalBusyUserId) return;
    this.portalBusyUserId = userId;
    this.clientsService.reInvitePortalUser(this.item.id, userId).subscribe({
      next: () => this.loadPortalUsers(this.item!.id),
      error: () => {},
      complete: () => (this.portalBusyUserId = null)
    });
  }

  setPortalUserStatus(userId: string, isActive: boolean): void {
    if (!this.item?.id || this.portalBusyUserId) return;
    this.portalBusyUserId = userId;
    this.clientsService.setPortalUserStatus(this.item.id, userId, { isActive }).subscribe({
      next: (updated) => {
        this.portalUsers = this.portalUsers.map((u) => (u.id === updated.id ? updated : u));
      },
      error: () => {},
      complete: () => (this.portalBusyUserId = null)
    });
  }
}
