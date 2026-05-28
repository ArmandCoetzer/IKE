import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/permittypes`;

export interface PermitTypeDto {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  isWorkAuthorisation?: boolean;
  triggersPermitTypeIdsJson?: string;
}

@Injectable({ providedIn: 'root' })
export class PermitTypesService {
  constructor(private http: HttpClient) {}

  list(isActive?: boolean): Observable<PermitTypeDto[]> {
    const params = isActive !== undefined ? '?isActive=' + isActive : '';
    return this.http.get<PermitTypeDto[]>(`${API}${params}`);
  }

  get(id: string): Observable<PermitTypeDto> {
    return this.http.get<PermitTypeDto>(`${API}/${id}`);
  }

  create(body: { name: string; description?: string }): Observable<PermitTypeDto> {
    return this.http.post<PermitTypeDto>(API, body);
  }
}
