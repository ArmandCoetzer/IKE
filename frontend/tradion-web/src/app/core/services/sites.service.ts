import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/sites`;

export interface SiteDto {
  id: string;
  name: string;
  address?: string;
  latitude?: number;
  longitude?: number;
  clientId?: string;
  clientName?: string;
  isActive: boolean;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class SitesService {
  constructor(private http: HttpClient) {}

  list(clientId?: string, isActive?: boolean): Observable<SiteDto[]> {
    const p = new URLSearchParams();
    if (clientId) p.set('clientId', clientId);
    if (isActive !== undefined) p.set('isActive', String(isActive));
    const q = p.toString();
    return this.http.get<SiteDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<SiteDto> {
    return this.http.get<SiteDto>(`${API}/${id}`);
  }

  create(request: CreateSiteRequest): Observable<SiteDto> {
    return this.http.post<SiteDto>(API, request);
  }

  update(id: string, request: UpdateSiteRequest): Observable<SiteDto> {
    return this.http.put<SiteDto>(`${API}/${id}`, request);
  }
}

export interface CreateSiteRequest {
  name: string;
  address?: string;
  latitude?: number;
  longitude?: number;
  clientId?: string;
}

export interface UpdateSiteRequest {
  name?: string;
  address?: string;
  latitude?: number;
  longitude?: number;
  clientId?: string;
  isActive?: boolean;
}
