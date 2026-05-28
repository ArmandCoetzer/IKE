import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/documents`;

@Injectable({ providedIn: 'root' })
export class DocumentsService {
  constructor(private http: HttpClient) {}

  /** GET quote PDF and return blob for download */
  getQuotePdf(quoteId: string): Observable<Blob> {
    return this.http.get(`${API}/quote/${quoteId}/pdf`, { responseType: 'blob' });
  }

  /** GET invoice PDF and return blob for download */
  getInvoicePdf(invoiceId: string): Observable<Blob> {
    return this.http.get(`${API}/invoice/${invoiceId}/pdf`, { responseType: 'blob' });
  }

  /** GET purchase order PDF and return blob for download */
  getPurchaseOrderPdf(poId: string): Observable<Blob> {
    return this.http.get(`${API}/purchase-order/${poId}/pdf`, { responseType: 'blob' });
  }

  /** GET job card PDF and return blob for download */
  getJobCardPdf(jobCardId: string): Observable<Blob> {
    return this.http.get(`${API}/job-card/${jobCardId}/pdf`, { responseType: 'blob' });
  }

  /** Email job card summary PDF to the site client (company contact email). */
  emailJobCardToClient(jobCardId: string): Observable<void> {
    return this.http.post<void>(`${API}/job-card/${jobCardId}/email-client`, {});
  }

  /** GET uploaded client PO file for a purchase order */
  getPurchaseOrderClientPO(poId: string): Observable<Blob> {
    return this.http.get(`${API}/purchase-order/${poId}/client-po`, { responseType: 'blob' });
  }
}
