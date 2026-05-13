import { Component, OnInit } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ReportsService, ProgressReportDto, ReportsSummaryDto, PermitByTypeStatusDto, ReportsIncidentDto } from '../../core/services/reports.service';
import { InvoiceDto } from '../../core/services/invoices.service';
import { TrainingService, ExpiringBadgeDto } from '../../core/services/training.service';
import { ClientsService, ClientDto } from '../../core/services/clients.service';
import { SitesService, SiteDto } from '../../core/services/sites.service';
import { AuthService } from '../../core/services/auth.service';
import { TableComponent } from '../../shared/table/table.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DecimalPipe, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.scss'
})
export class ReportsComponent implements OnInit {
  invoicesFrom = '';
  invoicesTo = '';
  invoicesReport: InvoiceDto[] = [];
  loadingReport = false;
  reportError: string | null = null;
  progressReport: ProgressReportDto | null = null;
  loadingProgress = false;
  downloadingPdf = false;
  expiringBadges: ExpiringBadgeDto[] = [];
  loadingBadges = false;
  badgesWithinDays = 30;
  badgesIncludeExpired = false;

  progressClientId = '';
  progressSiteId = '';
  progressFrom = '';
  progressTo = '';
  loadingProgressCsv = false;

  clients: ClientDto[] = [];
  sites: SiteDto[] = [];
  summary: ReportsSummaryDto | null = null;
  summaryFrom = '';
  summaryTo = '';
  summaryPeriod: '7' | '30' | '90' = '30';
  loadingSummary = false;
  permitsByType: PermitByTypeStatusDto[] = [];
  loadingPermits = false;
  incidents: ReportsIncidentDto[] = [];
  loadingIncidents = false;

  readonly tablePageSize = 10;
  pageProgress = 1;
  pageInvoices = 1;
  pagePermits = 1;
  pageIncidents = 1;
  pageBadges = 1;

  constructor(
    private reportsService: ReportsService,
    private trainingService: TrainingService,
    private clientsService: ClientsService,
    private sitesService: SitesService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    if (this.auth.role() === 'Client') {
      this.loadProgressReport();
    } else if (this.auth.hasPermission('ViewReports')) {
      this.loadExpiringBadges();
      this.loadClients();
      this.onProgressClientChange();
      this.loadSummary();
      this.loadPermitsByType();
      this.loadIncidents();
    }
  }

  loadClients(): void {
    this.clientsService.list(true).subscribe({
      next: (list) => { this.clients = list; },
      error: () => {}
    });
  }

  onProgressClientChange(): void {
    this.sites = [];
    this.progressSiteId = '';
    if (this.progressClientId) {
      this.sitesService.list(this.progressClientId, true).subscribe({ next: (list) => { this.sites = list; } });
    } else {
      this.sitesService.list(undefined, true).subscribe({ next: (list) => { this.sites = list; } });
    }
  }

  loadExpiringBadges(): void {
    this.loadingBadges = true;
    this.trainingService.getExpiringBadges({
      withinDays: this.badgesWithinDays,
      includeExpired: this.badgesIncludeExpired
    }).subscribe({
      next: (list) => {
        this.expiringBadges = list;
        this.loadingBadges = false;
        this.pageBadges = clampTablePage(this.pageBadges, this.expiringBadges.length, this.tablePageSize);
      },
      error: () => { this.loadingBadges = false; }
    });
  }

  loadProgressReport(): void {
    this.loadingProgress = true;
    this.reportsService.getProgressReport().subscribe({
      next: (r) => {
        this.progressReport = r;
        this.loadingProgress = false;
        const n = r.items?.length ?? 0;
        this.pageProgress = clampTablePage(this.pageProgress, n, this.tablePageSize);
      },
      error: () => (this.loadingProgress = false)
    });
  }

  private getProgressReportParams(): { companyId?: string; siteId?: string; from?: string; to?: string } {
    const params: { companyId?: string; siteId?: string; from?: string; to?: string } = {};
    if (this.progressClientId) params.companyId = this.progressClientId;
    if (this.progressSiteId) params.siteId = this.progressSiteId;
    if (this.progressFrom) params.from = new Date(this.progressFrom).toISOString().slice(0, 10);
    if (this.progressTo) params.to = new Date(this.progressTo).toISOString().slice(0, 10);
    return params;
  }

  private getSummaryDates(): { from: string; to: string } {
    const to = new Date();
    const days = parseInt(this.summaryPeriod, 10) || 30;
    const from = new Date(to);
    from.setDate(from.getDate() - days);
    return {
      from: from.toISOString().slice(0, 10),
      to: to.toISOString().slice(0, 10)
    };
  }

  downloadProgressCsvFiltered(): void {
    this.loadingProgressCsv = true;
    this.reportsService.getProgressReport(this.getProgressReportParams()).subscribe({
      next: (r) => {
        this.downloadCsvFromReport(r);
        this.loadingProgressCsv = false;
      },
      error: () => (this.loadingProgressCsv = false)
    });
  }

  private downloadCsvFromReport(r: ProgressReportDto): void {
    const headers = ['Job #', 'Request #', 'Client', 'Site', 'Description', 'Status', 'Created', 'Labour hrs', 'Amount'];
    const rows = r.items.map(i =>
      [
        i.jobCardNumber,
        i.serviceRequestNumber ?? '',
        (i.clientName ?? '').replace(/,/g, ' '),
        (i.siteName ?? '').replace(/,/g, ' '),
        (i.description ?? '').replace(/,/g, ' ').replace(/\n/g, ' '),
        i.status,
        i.createdAt ? new Date(i.createdAt).toISOString().slice(0, 10) : '',
        i.labourHours.toFixed(2),
        i.invoiceAmount.toFixed(2)
      ].join(',')
    );
    const csv = [headers.join(','), ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'progress-report.csv';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 60000);
  }

  downloadProgressPdf(): void {
    this.downloadingPdf = true;
    const params = this.auth.role() === 'Client' ? undefined : this.getProgressReportParams();
    this.reportsService.getProgressReportPdf(params).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `progress-report-${new Date().toISOString().slice(0, 10)}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
        this.downloadingPdf = false;
      },
      error: () => (this.downloadingPdf = false)
    });
  }

  get budgetProgressPercent(): number {
    const b = this.progressReport?.budget;
    if (!b || b.thresholdAmount <= 0) return 0;
    return Math.min(100, (b.spentAmount / b.thresholdAmount) * 100);
  }

  downloadProgressCsv(): void {
    if (!this.progressReport) return;
    this.downloadCsvFromReport(this.progressReport);
  }

  loadSummary(): void {
    this.loadingSummary = true;
    const customFrom = this.summaryFrom ? new Date(this.summaryFrom).toISOString().slice(0, 10) : undefined;
    const customTo = this.summaryTo ? new Date(this.summaryTo).toISOString().slice(0, 10) : undefined;
    const { from, to } = customFrom && customTo ? { from: customFrom, to: customTo } : this.getSummaryDates();
    this.reportsService.getSummary(from, to).subscribe({
      next: (s) => { this.summary = s; this.loadingSummary = false; },
      error: () => (this.loadingSummary = false)
    });
  }

  setSummaryPeriod(period: '7' | '30' | '90'): void {
    this.summaryPeriod = period;
    this.summaryFrom = '';
    this.summaryTo = '';
    this.pagePermits = 1;
    this.pageIncidents = 1;
    this.loadSummary();
    this.loadPermitsByType();
    this.loadIncidents();
  }

  applySummaryFilters(): void {
    this.pagePermits = 1;
    this.pageIncidents = 1;
    this.loadSummary();
    this.loadPermitsByType();
    this.loadIncidents();
  }

  loadPermitsByType(): void {
    this.loadingPermits = true;
    const { from, to } = this.summaryFrom && this.summaryTo
      ? { from: new Date(this.summaryFrom).toISOString().slice(0, 10), to: new Date(this.summaryTo).toISOString().slice(0, 10) }
      : this.getSummaryDates();
    this.reportsService.getPermitsByTypeStatus(from, to).subscribe({
      next: (list) => {
        this.permitsByType = list;
        this.loadingPermits = false;
        this.pagePermits = clampTablePage(this.pagePermits, this.permitsByType.length, this.tablePageSize);
      },
      error: () => (this.loadingPermits = false)
    });
  }

  loadIncidents(): void {
    this.loadingIncidents = true;
    const { from, to } = this.summaryFrom && this.summaryTo
      ? { from: new Date(this.summaryFrom).toISOString().slice(0, 10), to: new Date(this.summaryTo).toISOString().slice(0, 10) }
      : this.getSummaryDates();
    this.reportsService.getIncidents(from, to).subscribe({
      next: (list) => {
        this.incidents = list;
        this.loadingIncidents = false;
        this.pageIncidents = clampTablePage(this.pageIncidents, this.incidents.length, this.tablePageSize);
      },
      error: () => (this.loadingIncidents = false)
    });
  }

  runInvoicesReport(): void {
    this.pageInvoices = 1;
    this.reportError = null;
    this.loadingReport = true;
    const from = this.invoicesFrom ? new Date(this.invoicesFrom).toISOString().slice(0, 10) : undefined;
    const to = this.invoicesTo ? new Date(this.invoicesTo).toISOString().slice(0, 10) : undefined;
    this.reportsService.invoicesByPeriod(from, to).subscribe({
      next: (list) => {
        this.invoicesReport = list;
        this.loadingReport = false;
        this.pageInvoices = clampTablePage(this.pageInvoices, this.invoicesReport.length, this.tablePageSize);
      },
      error: () => {
        this.reportError = 'Failed to load report.';
        this.loadingReport = false;
      }
    });
  }
}
