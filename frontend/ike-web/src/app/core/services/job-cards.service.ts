import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/jobcards`;

export interface JobCardListDto {
  id: string;
  jobCardNumber: string;
  serviceRequestId?: string;
  serviceRequestNumber?: string;
  companyId?: string;
  siteId: string;
  siteName?: string;
  status: string;
  priority: number;
  dueDate?: string;
  createdAt: string;
  assignedTechnicianNames?: string;
  invoiceStatus?: string;
  blockedReason?: string;
}

@Injectable({ providedIn: 'root' })
export class JobCardsService {
  constructor(private http: HttpClient) {}

  private requestOptions(skipGlobalLoader = false): { headers?: HttpHeaders } {
    if (!skipGlobalLoader) return {};
    return { headers: new HttpHeaders({ 'x-skip-loader': '1' }) };
  }

  list(params?: { siteId?: string; status?: string; search?: string; page?: number; pageSize?: number }, skipGlobalLoader = false): Observable<{ items: JobCardListDto[]; total: number }> {
    const p = new URLSearchParams();
    if (params?.siteId) p.set('siteId', params.siteId);
    if (params?.status) p.set('status', params.status);
    if (params?.search) p.set('search', params.search);
    if (params?.page != null) p.set('page', String(params.page));
    if (params?.pageSize != null) p.set('pageSize', String(params.pageSize));
    const q = p.toString();
    const url = q ? `${API}?${q}` : API;
    return this.http.get<JobCardListDto[]>(url, { observe: 'response', ...this.requestOptions(skipGlobalLoader) }).pipe(
      map(res => ({
        items: res.body ?? [],
        total: parseInt(res.headers.get('X-Total-Count') ?? '0', 10)
      }))
    );
  }

  get(id: string): Observable<JobCardListDto> {
    return this.http.get<JobCardListDto>(`${API}/${id}`);
  }

  create(body: CreateJobCardRequest): Observable<JobCardListDto> {
    return this.http.post<JobCardListDto>(API, body);
  }

  update(id: string, body: UpdateJobCardRequest): Observable<JobCardListDto> {
    return this.http.patch<JobCardListDto>(`${API}/${id}`, body);
  }

  updateStatus(id: string, status: string): Observable<JobCardListDto> {
    return this.http.patch<JobCardListDto>(`${API}/${id}/status`, { status });
  }

  block(id: string, reason: string): Observable<JobCardListDto> {
    return this.http.patch<JobCardListDto>(`${API}/${id}/block`, { reason });
  }

  unblock(id: string): Observable<JobCardListDto> {
    return this.http.patch<JobCardListDto>(`${API}/${id}/unblock`, {});
  }
}

export interface CreateJobCardRequest {
  serviceRequestId?: string;
  siteId: string;
  status?: string;
}

export interface PlannedPartRequest {
  partId: string;
  quantity: number;
}

export interface UpdateJobCardRequest {
  serviceRequestId?: string | null;
  status?: string;
  description?: string | null;
  priority?: number;
  dueDate?: string | null;
  permitsRequired?: boolean;
  requiredPermitTypeId?: string | null;
  partsRequired?: boolean;
  plannedParts?: PlannedPartRequest[];
  activeJobPermitId?: string | null;
}
