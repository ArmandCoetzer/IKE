import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/servicerequests`;

export interface ServiceRequestAttachmentDto {
  id: string;
  fileName: string;
  createdAt: string;
}

export interface ServiceRequestDto {
  id: string;
  requestNumber: string;
  siteId: string;
  companyId?: string;
  siteName?: string;
  requestedByUserName?: string;
  description: string;
  priority: number;
  status: string;
  optionalDueDate?: string;
  createdAt: string;
  jobCardId?: string;
  jobCardNumber?: string;
  jobCardStatus?: string;
  assignedTechnicianNames?: string;
  penaltyFee?: number;
  penaltyNote?: string;
  attachments?: ServiceRequestAttachmentDto[];
}

@Injectable({ providedIn: 'root' })
export class ServiceRequestsService {
  constructor(private http: HttpClient) {}

  list(siteId?: string, status?: string): Observable<ServiceRequestDto[]> {
    const p = new URLSearchParams();
    if (siteId) p.set('siteId', siteId);
    if (status) p.set('status', status);
    const q = p.toString();
    return this.http.get<ServiceRequestDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<ServiceRequestDto> {
    return this.http.get<ServiceRequestDto>(`${API}/${id}`);
  }

  create(body: CreateServiceRequestRequest): Observable<ServiceRequestDto> {
    return this.http.post<ServiceRequestDto>(API, body);
  }

  updateStatus(id: string, status: string): Observable<ServiceRequestDto> {
    return this.http.patch<ServiceRequestDto>(`${API}/${id}/status`, { status });
  }

  update(id: string, body: UpdateServiceRequestRequest): Observable<ServiceRequestDto> {
    return this.http.put<ServiceRequestDto>(`${API}/${id}`, body);
  }

  uploadAttachments(id: string, files: File[]): Observable<void> {
    const form = new FormData();
    files.forEach(f => form.append('files', f));
    return this.http.post<void>(`${API}/${id}/attachments`, form);
  }

  getAttachmentFile(id: string, attachmentId: string): Observable<Blob> {
    return this.http.get(`${API}/${id}/attachments/${attachmentId}/file`, { responseType: 'blob' });
  }
}

export interface UpdateServiceRequestRequest {
  siteId: string;
  description: string;
  priority: number;
  optionalDueDate?: string;
  penaltyFee?: number | null;
  penaltyNote?: string | null;
}

export interface CreateServiceRequestRequest {
  siteId: string;
  description: string;
  priority: number;
  optionalDueDate?: string;
}
