import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/jobpermits`;

@Injectable({ providedIn: 'root' })
export class JobPermitsService {
  constructor(private http: HttpClient) {}

  requestPermit(
    jobCardId: string,
    options?: { permitTypeId?: string; masterPermitId?: string }
  ): Observable<void> {
    const body: { jobCardId: string; permitTypeId?: string; masterPermitId?: string } = { jobCardId };
    if (options?.permitTypeId) body.permitTypeId = options.permitTypeId;
    if (options?.masterPermitId) body.masterPermitId = options.masterPermitId;
    return this.http.post<void>(API, body);
  }

  uploadAttachment(permitId: string, file: File): Observable<void> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<void>(`${API}/${permitId}/upload`, formData);
  }

  downloadAttachment(attachmentId: string, fileName: string): Observable<Blob> {
    return this.http.get(`${API}/attachments/${attachmentId}`, { responseType: 'blob' });
  }

  emailClient(permitId: string): Observable<void> {
    return this.http.post<void>(`${API}/${permitId}/email-client`, {});
  }

  /** Single PDF: form, checklist, file list, embedded client signature (child permits only). */
  getDocumentationPdf(permitId: string): Observable<Blob> {
    return this.http.get(`${API}/${permitId}/documentation-pdf`, { responseType: 'blob' });
  }

  setPaperPermitNumber(permitId: string, paperPermitNumber: string): Observable<void> {
    return this.http.patch<void>(`${API}/${permitId}/paper-number`, { paperPermitNumber });
  }

  paperClientSignOff(permitId: string): Observable<void> {
    return this.http.patch<void>(`${API}/${permitId}/paper-client-sign-off`, {});
  }
}
