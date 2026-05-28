import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/work-authorizations/master-permit`;

@Injectable({ providedIn: 'root' })
export class WorkAuthorizationsService {
  constructor(private http: HttpClient) {}

  getDocumentHtml(permitId: string): Observable<string> {
    return this.http.get(`${API}/${permitId}/document`, { responseType: 'text' });
  }

  downloadDocumentPdf(permitId: string): Observable<Blob> {
    return this.http.get(`${API}/${permitId}/document-pdf`, { responseType: 'blob' });
  }

  emailClient(permitId: string): Observable<void> {
    return this.http.post<void>(`${API}/${permitId}/email-client`, {});
  }
}
