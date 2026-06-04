import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/invoices`;

export interface InvoiceLineItemDto {
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

export interface InvoiceDto {
  id: string;
  invoiceNumber: string;
  jobCardId: string;
  jobCardNumber?: string;
  quoteId?: string;
  quoteNumber?: string;
  clientId?: string;
  clientName?: string;
  siteId: string;
  siteName?: string;
  amount: number;
  currency: string;
  status: string;
  dueDate: string;
  sentAt?: string;
  paidAt?: string;
  createdAt: string;
  notes?: string;
  lineItems: InvoiceLineItemDto[];
  partsConfirmed?: boolean;
  isUploaded?: boolean;
  uploadedFileName?: string;
  uploadedContentType?: string;
  uploadedAt?: string;
  extractedInvoiceNumber?: string;
  extractedText?: string;
}

@Injectable({ providedIn: 'root' })
export class InvoicesService {
  constructor(private http: HttpClient) {}

  list(clientId?: string, siteId?: string, status?: string): Observable<InvoiceDto[]> {
    const p = new URLSearchParams();
    if (clientId) p.set('clientId', clientId);
    if (siteId) p.set('siteId', siteId);
    if (status) p.set('status', status);
    const q = p.toString();
    return this.http.get<InvoiceDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<InvoiceDto> {
    return this.http.get<InvoiceDto>(`${API}/${id}`);
  }

  create(body: CreateInvoiceRequest): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(API, body);
  }

  upload(body: UploadInvoiceRequest): Observable<InvoiceDto> {
    const form = new FormData();
    form.append('jobCardId', body.jobCardId);
    if (body.quoteId) form.append('quoteId', body.quoteId);
    if (body.clientId) form.append('clientId', body.clientId);
    form.append('siteId', body.siteId);
    if (body.dueDate) form.append('dueDate', body.dueDate);
    if (body.notes) form.append('notes', body.notes);
    form.append('file', body.file);
    return this.http.post<InvoiceDto>(`${API}/upload`, form);
  }

  getUploadedFile(id: string): Observable<Blob> {
    return this.http.get(`${API}/${id}/uploaded-file`, { responseType: 'blob' });
  }

  send(id: string, toEmail?: string, attachPdf = true): Observable<void> {
    const p = new URLSearchParams();
    if (toEmail) p.set('toEmail', toEmail);
    p.set('attachPdf', String(attachPdf));
    return this.http.post<void>(`${API}/${id}/send?${p.toString()}`, {});
  }

  sendReminder(id: string, toEmail?: string, attachPdf = true): Observable<void> {
    const p = new URLSearchParams();
    if (toEmail) p.set('toEmail', toEmail);
    p.set('attachPdf', String(attachPdf));
    return this.http.post<void>(`${API}/${id}/send-reminder?${p.toString()}`, {});
  }

  markPaid(id: string): Observable<InvoiceDto> {
    return this.http.patch<InvoiceDto>(`${API}/${id}/mark-paid`, {});
  }

  update(id: string, body: UpdateInvoiceRequest): Observable<InvoiceDto> {
    return this.http.put<InvoiceDto>(`${API}/${id}`, body);
  }

  confirmParts(id: string, body?: ConfirmPartsRequest): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(`${API}/${id}/confirm-parts`, body ?? {});
  }
}

export interface UpdateInvoiceRequest {
  dueDate: string;
  notes?: string;
  lineItems?: InvoiceLineItemInput[];
}

export interface ConfirmPartsRequest {
  lineItems?: InvoiceLineItemInput[];
}

export interface InvoiceLineItemInput {
  lineType: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent?: number;
  partId?: string;
}

export interface CreateInvoiceRequest {
  jobCardId: string;
  quoteId?: string;
  clientId?: string;
  siteId: string;
  amount: number;
  dueDate: string;
  currency?: string;
  notes?: string;
  lineItems?: InvoiceLineItemInput[];
}

export interface UploadInvoiceRequest {
  jobCardId: string;
  quoteId?: string;
  clientId?: string;
  siteId: string;
  dueDate?: string;
  notes?: string;
  file: File;
}
