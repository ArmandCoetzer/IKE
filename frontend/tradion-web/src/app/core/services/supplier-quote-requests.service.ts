import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/supplierquoterequests`;

export interface SupplierQuoteRequestDto {
  id: string;
  supplierId: string;
  supplierName: string;
  partId?: string;
  partName?: string;
  jobCardId?: string;
  jobCardNumber?: string;
  requestedQuantity?: number;
  status: string;
  notes?: string;
  createdAt: string;
}

export interface CreateSupplierQuoteRequest {
  supplierId: string;
  partId: string;
  requestedQuantity?: number;
  notes?: string;
}

export interface BuildSupplierQuoteDraftRequest {
  supplierId: string;
  items: { partId: string; requestedQuantity?: number }[];
  notes?: string;
}

export interface SupplierQuoteEmailDraftItemDto {
  partId: string;
  partName: string;
  unit?: string;
  requiredQuantity: number;
  reorderLevel: number;
  stockQuantity: number;
  requestedQuantity: number;
}

export interface SupplierQuoteEmailDraftDto {
  supplierId: string;
  supplierName: string;
  toEmail: string;
  subject: string;
  body: string;
  items: SupplierQuoteEmailDraftItemDto[];
}

export interface SupplierQuoteEmailDraftsResponse {
  jobCardId: string;
  drafts: SupplierQuoteEmailDraftDto[];
}

export interface SendSupplierQuoteDraftRequest {
  jobCardId?: string;
  supplierId: string;
  toEmail: string;
  subject: string;
  body: string;
  items: { partId: string; requestedQuantity: number; notes?: string }[];
}

@Injectable({ providedIn: 'root' })
export class SupplierQuoteRequestsService {
  constructor(private http: HttpClient) {}

  list(status?: string): Observable<SupplierQuoteRequestDto[]> {
    const q = status ? `?status=${encodeURIComponent(status)}` : '';
    return this.http.get<SupplierQuoteRequestDto[]>(`${API}${q}`);
  }

  create(body: CreateSupplierQuoteRequest): Observable<SupplierQuoteRequestDto> {
    return this.http.post<SupplierQuoteRequestDto>(API, body);
  }

  buildDraft(body: BuildSupplierQuoteDraftRequest): Observable<SupplierQuoteEmailDraftDto> {
    return this.http.post<SupplierQuoteEmailDraftDto>(`${API}/draft`, body);
  }

  updateStatus(id: string, status: string): Observable<void> {
    return this.http.patch<void>(`${API}/${id}/status`, { status });
  }

  buildJobCardEmailDrafts(jobCardId: string): Observable<SupplierQuoteEmailDraftsResponse> {
    return this.http.post<SupplierQuoteEmailDraftsResponse>(`${API}/job-cards/${jobCardId}/email-drafts`, {});
  }

  sendDraft(body: SendSupplierQuoteDraftRequest): Observable<{ supplierId: string; supplierName: string; createdRequestIds: string[] }> {
    return this.http.post<{ supplierId: string; supplierName: string; createdRequestIds: string[] }>(`${API}/send-draft`, body);
  }
}
