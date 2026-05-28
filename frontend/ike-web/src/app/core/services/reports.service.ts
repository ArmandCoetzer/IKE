import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';
import { InvoiceDto } from './invoices.service';

const API = `${API_BASE}/reports`;

export interface ProgressReportItemDto {
  jobCardId: string;
  jobCardNumber: string;
  serviceRequestNumber?: string;
  clientName?: string;
  siteName?: string;
  description?: string;
  status: string;
  createdAt: string;
  labourHours: number;
  totalAmount: string;
  invoiceAmount: number;
  hasInvoice: boolean;
}

export interface ReportsSummaryDto {
  permitsPending: number;
  permitsApproved: number;
  incidents: number;
  activeJobCards: number;
  completedJobCards: number;
}

export interface PermitByTypeStatusDto {
  typeName: string;
  status: string;
  count: number;
}

export interface ReportsIncidentDto {
  id: string;
  jobCardId: string;
  jobCardNumber?: string;
  siteName?: string;
  description: string;
  severity: string;
  reportedByUserName?: string;
  createdAt: string;
}

export interface ClientBudgetSummaryDto {
  thresholdAmount: number;
  spentAmount: number;
  currency: string;
  workPaused: boolean;
}

export interface ProgressReportDto {
  items: ProgressReportItemDto[];
  totalLabourHours: number;
  totalAmount: number;
  budget?: ClientBudgetSummaryDto;
}

@Injectable({ providedIn: 'root' })
export class ReportsService {
  constructor(private http: HttpClient) {}

  invoicesByPeriod(from?: string, to?: string): Observable<InvoiceDto[]> {
    const params = new URLSearchParams();
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    const q = params.toString();
    return this.http.get<InvoiceDto[]>(q ? `${API}/invoices-by-period?${q}` : `${API}/invoices-by-period`);
  }

  getProgressReport(params?: { companyId?: string; siteId?: string; from?: string; to?: string }): Observable<ProgressReportDto> {
    const p = new URLSearchParams();
    if (params?.companyId) p.set('companyId', params.companyId);
    if (params?.siteId) p.set('siteId', params.siteId);
    if (params?.from) p.set('from', params.from);
    if (params?.to) p.set('to', params.to);
    const q = p.toString();
    return this.http.get<ProgressReportDto>(q ? `${API}/progress?${q}` : `${API}/progress`);
  }

  getProgressReportPdf(params?: { companyId?: string; siteId?: string; from?: string; to?: string }): Observable<Blob> {
    const p = new URLSearchParams();
    if (params?.companyId) p.set('companyId', params.companyId);
    if (params?.siteId) p.set('siteId', params.siteId);
    if (params?.from) p.set('from', params.from);
    if (params?.to) p.set('to', params.to);
    const q = p.toString();
    return this.http.get(`${API}/progress/pdf${q ? '?' + q : ''}`, { responseType: 'blob' });
  }

  getSummary(from?: string, to?: string): Observable<ReportsSummaryDto> {
    const p = new URLSearchParams();
    if (from) p.set('from', from);
    if (to) p.set('to', to);
    const q = p.toString();
    return this.http.get<ReportsSummaryDto>(q ? `${API}/summary?${q}` : `${API}/summary`);
  }

  getPermitsByTypeStatus(from?: string, to?: string): Observable<PermitByTypeStatusDto[]> {
    const p = new URLSearchParams();
    if (from) p.set('from', from);
    if (to) p.set('to', to);
    const q = p.toString();
    return this.http.get<PermitByTypeStatusDto[]>(q ? `${API}/permits-by-type-status?${q}` : `${API}/permits-by-type-status`);
  }

  getIncidents(from?: string, to?: string): Observable<ReportsIncidentDto[]> {
    const p = new URLSearchParams();
    if (from) p.set('from', from);
    if (to) p.set('to', to);
    const q = p.toString();
    return this.http.get<ReportsIncidentDto[]>(q ? `${API}/incidents?${q}` : `${API}/incidents`);
  }
}
