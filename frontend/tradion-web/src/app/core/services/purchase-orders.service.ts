import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/purchaseorders`;

export interface PurchaseOrderDto {
  id: string;
  poNumber: string;
  clientPONumber?: string;
  hasClientPOFile: boolean;
  clientId: string;
  clientName?: string;
  siteId: string;
  siteName?: string;
  jobCardId?: string;
  serviceRequestId?: string;
  quoteId?: string;
  amount: number;
  currency: string;
  status: string;
  notes?: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrdersService {
  constructor(private http: HttpClient) {}

  list(clientId?: string, siteId?: string, status?: string): Observable<PurchaseOrderDto[]> {
    const p = new URLSearchParams();
    if (clientId) p.set('clientId', clientId);
    if (siteId) p.set('siteId', siteId);
    if (status) p.set('status', status);
    const q = p.toString();
    return this.http.get<PurchaseOrderDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<PurchaseOrderDto> {
    return this.http.get<PurchaseOrderDto>(`${API}/${id}`);
  }

  updateStatus(id: string, status: string): Observable<PurchaseOrderDto> {
    return this.http.patch<PurchaseOrderDto>(`${API}/${id}/status`, { status });
  }

  create(body: CreatePurchaseOrderRequest): Observable<PurchaseOrderDto> {
    return this.http.post<PurchaseOrderDto>(API, body);
  }

  update(id: string, body: UpdatePurchaseOrderRequest): Observable<PurchaseOrderDto> {
    return this.http.put<PurchaseOrderDto>(`${API}/${id}`, body);
  }

  uploadClientPO(id: string, file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<void>(`${API}/${id}/client-po-upload`, form);
  }
}

export interface UpdatePurchaseOrderRequest {
  clientPONumber?: string;
  amount: number;
  currency?: string;
  notes?: string;
}

export interface CreatePurchaseOrderRequest {
  clientId: string;
  siteId: string;
  jobCardId?: string;
  serviceRequestId?: string;
  quoteId?: string;
  amount: number;
  currency?: string;
  clientPONumber?: string;
  notes?: string;
}
