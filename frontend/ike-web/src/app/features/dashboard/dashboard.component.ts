import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { PartsService, PartDto } from '../../core/services/parts.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface DashboardIssue {
  id: string;
  title: string;
  detail: string;
  severity: 'danger' | 'warning' | 'info';
  actionLabel: string;
  routerLink: string | any[];
  queryParams?: Record<string, string | number | undefined>;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  unprocessedCount = 0;
  ongoingCount = 0;
  overdueInvoicesCount = 0;
  requestsWithoutJobCard = 0;
  completedJobsWithoutInvoice = 0;
  lowStockPartsCount = 0;
  completedJobsCount = 0;
  parts: PartDto[] = [];

  constructor(
    public auth: AuthService,
    private dashboardService: DashboardService,
    private partsService: PartsService
  ) {}

  ngOnInit(): void {
    this.dashboardService.getCounts().subscribe({
      next: (c) => {
        this.unprocessedCount = c.unprocessedRequests;
        this.ongoingCount = c.ongoingJobCards;
        this.overdueInvoicesCount = c.overdueInvoices ?? 0;
        this.requestsWithoutJobCard = c.requestsWithoutJobCard ?? 0;
        this.completedJobsWithoutInvoice = c.completedJobsWithoutInvoice ?? 0;
        this.lowStockPartsCount = c.lowStockPartsCount ?? 0;
        this.completedJobsCount = c.completedJobsCount ?? 0;
      }
    });
    if (this.auth.role() !== 'Client' && this.auth.role() !== 'Technician') {
      this.partsService.list().subscribe({
        next: (parts) => (this.parts = parts)
      });
    }
  }

  guaranteedQuantity(p: PartDto): number {
    return p.availableQuantity ?? p.quantity ?? 0;
  }

  stockRequestQuantity(p: PartDto): number | undefined {
    const reorderNeeded = (p.reorderLevel ?? 0) - this.guaranteedQuantity(p);
    const activeJobShortfall = (p.reservedForActiveJobsQuantity ?? 0) - (p.quantity ?? 0);
    const needed = Math.max(reorderNeeded, activeJobShortfall, 0);
    return needed > 0 ? needed : undefined;
  }

  hasSupplier(p: PartDto): boolean {
    return !!p.supplierId || (p.supplierIds?.length ?? 0) > 0;
  }

  canRequestStock(p: PartDto): boolean {
    return this.hasSupplier(p) && !!p.hasSupplierEmail;
  }

  canShowRequestStock(p: PartDto): boolean {
    return p.isLowStock && this.canRequestStock(p) && (p.reorderLevel ?? 0) > 0 && !!p.unit?.trim();
  }

  get dashboardIssues(): DashboardIssue[] {
    if (this.auth.role() === 'Client' || this.auth.role() === 'Technician') return [];
    const issues: DashboardIssue[] = [];
    if (this.requestsWithoutJobCard > 0) {
      issues.push({
        id: 'requests-without-job-card',
        title: 'Requests need job cards',
        detail: `${this.requestsWithoutJobCard} active request${this.requestsWithoutJobCard === 1 ? '' : 's'} without a job card.`,
        severity: 'warning',
        actionLabel: 'Go to requests',
        routerLink: '/service-requests'
      });
    }
    if (this.overdueInvoicesCount > 0) {
      issues.push({
        id: 'overdue-invoices',
        title: 'Overdue invoices',
        detail: `${this.overdueInvoicesCount} invoice${this.overdueInvoicesCount === 1 ? '' : 's'} overdue.`,
        severity: 'danger',
        actionLabel: 'View invoices',
        routerLink: '/invoices'
      });
    }

    for (const p of this.parts.filter(part => !part.isLabour)) {
      const guaranteed = this.guaranteedQuantity(p);
      if (p.isLowStock) {
        const canRequest = this.canShowRequestStock(p);
        issues.push({
          id: `low-stock-${p.id}`,
          title: `Low stock: ${p.name}`,
          detail: `Guaranteed stock ${guaranteed}, quantity ${p.quantity}, active jobs need ${p.reservedForActiveJobsQuantity ?? 0}, reorder level ${p.reorderLevel}.`,
          severity: 'danger',
          actionLabel: canRequest ? 'Request stock' : 'Add supplier',
          routerLink: canRequest ? ['/supplier-quote-requests/new'] : ['/parts', p.id, 'edit'],
          queryParams: canRequest
            ? {
                partId: p.id,
                supplierId: p.supplierId || '',
                requestedQuantity: this.stockRequestQuantity(p),
                notes: `Low stock request: ${p.name} (have ${guaranteed}, reorder ${p.reorderLevel})`,
                returnTo: '/dashboard'
              }
            : { returnTo: '/dashboard' }
        });
      }
      if ((p.reorderLevel ?? 0) <= 0) {
        issues.push({
          id: `missing-reorder-${p.id}`,
          title: `Reorder level missing: ${p.name}`,
          detail: 'Set a reorder level so the system can judge whether stock is sufficient.',
          severity: 'warning',
          actionLabel: 'Fix item',
          routerLink: ['/parts', p.id, 'edit'],
          queryParams: { returnTo: '/dashboard' }
        });
      }
      if (!p.unit?.trim()) {
        issues.push({
          id: `missing-unit-${p.id}`,
          title: `Unit of measurement missing: ${p.name}`,
          detail: 'Set the unit of measurement so stock quantities are clear when requesting and invoicing parts.',
          severity: 'warning',
          actionLabel: 'Fix item',
          routerLink: ['/parts', p.id, 'edit'],
          queryParams: { returnTo: '/dashboard' }
        });
      }
      if (!this.hasSupplier(p)) {
        issues.push({
          id: `missing-supplier-${p.id}`,
          title: `Supplier missing: ${p.name}`,
          detail: 'This part cannot be used for supplier stock requests until a supplier is linked.',
          severity: 'warning',
          actionLabel: 'Add supplier',
          routerLink: ['/parts', p.id, 'edit'],
          queryParams: { returnTo: '/dashboard' }
        });
      } else if (!p.hasSupplierEmail) {
        issues.push({
          id: `missing-supplier-email-${p.id}`,
          title: `Supplier missing: ${p.name}`,
          detail: 'This part has a supplier link, but no supplier email for stock request emails.',
          severity: 'warning',
          actionLabel: 'Fix supplier',
          routerLink: ['/parts', p.id, 'edit'],
          queryParams: { returnTo: '/dashboard' }
        });
      }
    }
    return issues;
  }
}
