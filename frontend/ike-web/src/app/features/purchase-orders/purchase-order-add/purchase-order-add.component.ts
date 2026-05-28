import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PurchaseOrdersService, CreatePurchaseOrderRequest } from '../../../core/services/purchase-orders.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

type POSource = 'us' | 'client';

@Component({
  selector: 'app-purchase-order-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './purchase-order-add.component.html',
  styleUrl: './purchase-order-add.component.scss'
})
export class PurchaseOrderAddComponent implements OnInit {
  clientId: string | null = null;
  siteId: string | null = null;
  jobCardId: string | null = null;
  returnTo: string | null = null;
  quoteId: string | null = null;
  amount: number | null = null;
  poSource: POSource = 'us';
  clientPONumber = '';
  clientPOFile: File | null = null;
  notes = '';
  clients: ClientDto[] = [];
  sites: SiteDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  goBack(): void {
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/purchase-orders']);
  }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private purchaseOrdersService: PurchaseOrdersService,
    private sitesService: SitesService,
    private clientsService: ClientsService
  ) {}

  get fromQuote(): boolean {
    return !!this.quoteId;
  }

  onClientChange(): void {
    this.siteId = null;
    if (this.clientId) {
      this.sitesService.list(this.clientId, true).subscribe({ next: (s) => (this.sites = s) });
    } else {
      this.sites = [];
    }
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['quoteId']) this.quoteId = q['quoteId'];
    if (q['jobCardId']) this.jobCardId = q['jobCardId'];
    if (q['returnTo']) this.returnTo = sanitizeInternalReturnTo(q['returnTo']);
    if (q['clientId']) this.clientId = q['clientId'];
    if (q['siteId']) this.siteId = q['siteId'];
    if (q['amount']) this.amount = Number(q['amount']);
    if (q['notes']) this.notes = q['notes'];
    this.loading = true;
    this.clientsService.list().subscribe({
      next: (list) => {
        this.clients = list;
        this.sitesService.list(this.clientId ?? undefined, true).subscribe({
          next: (sites) => {
            this.sites = sites;
            if (this.siteId && !this.clientId) {
              const site = sites.find(s => s.id === this.siteId);
              if (site?.clientId) this.clientId = site.clientId;
            }
            this.loading = false;
          },
          error: () => (this.loading = false)
        });
      },
      error: () => (this.loading = false)
    });
  }

  onClientPOFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.clientPOFile = input.files?.[0] ?? null;
  }

  save(): void {
    this.error = null;
    if (!this.clientId || !this.siteId) {
      this.error = 'Client and site are required.';
      return;
    }
    if (this.amount == null || this.amount < 0) {
      this.error = 'Amount is required.';
      return;
    }
    const body: CreatePurchaseOrderRequest = {
      clientId: this.clientId,
      siteId: this.siteId,
      jobCardId: this.jobCardId || undefined,
      quoteId: this.quoteId || undefined,
      amount: this.amount,
      currency: 'ZAR',
      clientPONumber: this.poSource === 'client' && this.clientPONumber.trim() ? this.clientPONumber.trim() : undefined,
      notes: this.notes.trim() || undefined
    };
    this.submitting = true;
    this.purchaseOrdersService.create(body).subscribe({
      next: (po) => {
        if (this.poSource === 'client' && this.clientPOFile) {
          this.purchaseOrdersService.uploadClientPO(po.id, this.clientPOFile).subscribe({
            next: () => {
              this.submitting = false;
              const back = sanitizeInternalReturnTo(this.returnTo);
              if (back) this.router.navigateByUrl(back);
              else this.router.navigate(['/purchase-orders', po.id]);
            },
            error: (err) => {
              this.submitting = false;
              this.error = err.error?.message || 'PO created but client PO file upload failed.';
            }
          });
        } else {
          this.submitting = false;
          const back = sanitizeInternalReturnTo(this.returnTo);
          if (back) this.router.navigateByUrl(back);
          else this.router.navigate(['/purchase-orders', po.id]);
        }
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to create purchase order.';
      }
    });
  }
}
