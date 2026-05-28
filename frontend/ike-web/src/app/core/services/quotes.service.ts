import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/quotes`;

export interface QuoteLineItemDto {
  id?: string;
  lineType: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  lineSubtotal?: number;
  lineDiscountAmount?: number;
  lineTotal?: number;
  partId?: string;
  partName?: string;
}

export interface QuoteDto {
  id: string;
  quoteNumber: string;
  clientId: string;
  clientName?: string;
  siteId: string;
  siteName?: string;
  jobCardId?: string;
  serviceRequestId?: string;
  amount: number;
  subtotalAmount?: number;
  discountAmount?: number;
  currency: string;
  description: string;
  isUploaded?: boolean;
  uploadedFileName?: string;
  uploadedContentType?: string;
  uploadedAt?: string;
  extractedQuoteNumber?: string;
  extractedSupplierName?: string;
  extractedText?: string;
  discountMode?: 'None' | 'Global' | 'PerItem';
  globalDiscountPercent?: number;
  notes?: string;
  status: string;
  validUntil?: string;
  sentAt?: string;
  createdAt: string;
  linkedPurchaseOrderId?: string;
  linkedPurchaseOrderNumber?: string;
  lineItems?: QuoteLineItemDto[];
}

@Injectable({ providedIn: 'root' })
export class QuotesService {
  constructor(private http: HttpClient) {}

  list(clientId?: string, siteId?: string, status?: string): Observable<QuoteDto[]> {
    const p = new URLSearchParams();
    if (clientId) p.set('clientId', clientId);
    if (siteId) p.set('siteId', siteId);
    if (status) p.set('status', status);
    const q = p.toString();
    return this.http.get<QuoteDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<QuoteDto> {
    return this.http.get<QuoteDto>(`${API}/${id}`);
  }

  create(body: CreateQuoteRequest): Observable<QuoteDto> {
    return this.http.post<QuoteDto>(API, body);
  }

  upload(body: UploadQuoteRequest): Observable<QuoteDto> {
    const form = new FormData();
    form.append('clientId', body.clientId);
    form.append('siteId', body.siteId);
    if (body.jobCardId) form.append('jobCardId', body.jobCardId);
    if (body.serviceRequestId) form.append('serviceRequestId', body.serviceRequestId);
    form.append('file', body.file);
    return this.http.post<QuoteDto>(`${API}/upload`, form);
  }

  getUploadedFile(id: string): Observable<Blob> {
    return this.http.get(`${API}/${id}/uploaded-file`, { responseType: 'blob' });
  }

  linkToJobCard(quoteId: string, jobCardId: string): Observable<QuoteDto> {
    return this.http.post<QuoteDto>(`${API}/${quoteId}/link-job-card`, { jobCardId });
  }

  send(id: string, toEmail?: string, attachPdf = true): Observable<void> {
    const p = new URLSearchParams();
    if (toEmail) p.set('toEmail', toEmail);
    p.set('attachPdf', String(attachPdf));
    return this.http.post<void>(`${API}/${id}/send?${p.toString()}`, {});
  }

  /** Client (or staff with ManagePurchaseOrders) accepts a quote in Sent status. */
  accept(id: string): Observable<void> {
    return this.http.post<void>(`${API}/${id}/accept`, {});
  }

  updateStatus(id: string, status: string): Observable<QuoteDto> {
    return this.http.patch<QuoteDto>(`${API}/${id}/status`, { status });
  }

  update(id: string, body: UpdateQuoteRequest): Observable<QuoteDto> {
    return this.http.put<QuoteDto>(`${API}/${id}`, body);
  }
}

export interface UpdateQuoteRequest {
  amount: number;
  currency?: string;
  description: string;
  discountMode?: 'None' | 'Global' | 'PerItem';
  globalDiscountPercent?: number;
  notes?: string;
  validUntil?: string;
  lineItems?: QuoteLineItemInput[];
}

export interface QuoteLineItemInput {
  lineType: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  partId?: string;
}

export interface CreateQuoteRequest {
  clientId: string;
  siteId: string;
  jobCardId?: string;
  serviceRequestId?: string;
  amount: number;
  currency?: string;
  description: string;
  deferPricing?: boolean;
  discountMode?: 'None' | 'Global' | 'PerItem';
  globalDiscountPercent?: number;
  notes?: string;
  validUntil?: string;
  lineItems?: QuoteLineItemInput[];
}

export interface UploadQuoteRequest {
  clientId: string;
  siteId: string;
  jobCardId?: string;
  serviceRequestId?: string;
  file: File;
}
