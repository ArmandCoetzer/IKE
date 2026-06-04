import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/clients`;

export interface ClientPortalUserDto {
  id: string;
  email: string;
  fullName?: string;
  isActive: boolean;
  registrationStatus?: string;
}

export interface ClientDto {
  id: string;
  companyName: string;
  contactName?: string;
  phone?: string;
  email?: string;
  userId?: string;
  userEmail?: string;
  /** false when email already registered globally — company created without a new portal user */
  portalUserCreated?: boolean;
  portalMessage?: string;
  isActive: boolean;
  createdAt: string;
}

export interface SetClientPortalUserStatusRequest {
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class ClientsService {
  constructor(private http: HttpClient) {}

  list(isActive?: boolean): Observable<ClientDto[]> {
    const params = isActive !== undefined ? '?isActive=' + isActive : '';
    return this.http.get<ClientDto[]>(`${API}${params}`);
  }

  get(id: string): Observable<ClientDto> {
    return this.http.get<ClientDto>(`${API}/${id}`);
  }

  getPortalUsers(id: string): Observable<ClientPortalUserDto[]> {
    return this.http.get<ClientPortalUserDto[]>(`${API}/${id}/portal-users`);
  }

  reInvitePortalUser(clientId: string, userId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API}/${clientId}/portal-users/${userId}/re-invite`, {});
  }

  setPortalUserStatus(clientId: string, userId: string, body: SetClientPortalUserStatusRequest): Observable<ClientPortalUserDto> {
    return this.http.put<ClientPortalUserDto>(`${API}/${clientId}/portal-users/${userId}/status`, body);
  }

  create(body: CreateClientRequest): Observable<ClientDto> {
    return this.http.post<ClientDto>(API, body);
  }

  update(id: string, body: UpdateClientRequest): Observable<ClientDto> {
    return this.http.put<ClientDto>(`${API}/${id}`, body);
  }

  downloadImportTemplate(): Observable<Blob> {
    return this.http.get(`${API}/import-template`, { responseType: 'blob' });
  }

  importPreview(file: File): Observable<ClientImportResultDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ClientImportResultDto>(`${API}/import-preview`, form);
  }

  importCommit(rows: ClientImportRowDto[]): Observable<ClientImportResultDto> {
    return this.http.post<ClientImportResultDto>(`${API}/import-commit`, { rows });
  }
}

export interface CreateClientRequest {
  companyName: string;
  contactName?: string;
  phone?: string;
  email?: string;
  userId?: string;
}

export interface UpdateClientRequest {
  companyName?: string;
  contactName?: string;
  phone?: string;
  email?: string;
  userId?: string;
  isActive?: boolean;
}

export interface ClientImportRowDto {
  rowNumber: number;
  companyName: string;
  contactName?: string;
  phone?: string;
  email?: string;
  siteName: string;
  siteAddress?: string;
  errors: string[];
  createdClientId?: string;
  createdSiteId?: string;
}

export interface ClientImportResultDto {
  rows: ClientImportRowDto[];
  failedRows: ClientImportRowDto[];
  totalRows: number;
  successCount: number;
  failedCount: number;
}
