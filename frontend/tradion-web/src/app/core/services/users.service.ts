import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/users`;

export interface UserListDto {
  id: string;
  email: string;
  fullName: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  occupation?: string;
  role?: string;
  isActive: boolean;
  createdAt: string;
  siteId?: string;
  siteName?: string;
  companyName?: string;
  registrationStatus?: string;
}

export interface UpdateUserRequest {
  firstName?: string;
  lastName?: string;
  phone?: string;
  occupation?: string;
  role?: string;
  isActive?: boolean;
  password?: string;
  siteId?: string | null;
  clearSite?: boolean;
}

@Injectable({ providedIn: 'root' })
export class UsersService {
  constructor(private http: HttpClient) {}

  list(role?: string, isActive?: boolean): Observable<UserListDto[]> {
    const p = new URLSearchParams();
    if (role) p.set('role', role);
    if (isActive !== undefined) p.set('isActive', String(isActive));
    const q = p.toString();
    return this.http.get<UserListDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<UserListDto> {
    return this.http.get<UserListDto>(`${API}/${id}`);
  }

  getRoles(excludeClient = false): Observable<string[]> {
    const q = excludeClient ? '?excludeClient=true' : '';
    return this.http.get<string[]>(`${API}/roles${q}`);
  }

  create(request: {
    email: string;
    firstName: string;
    lastName: string;
    phone?: string;
    occupation?: string;
    password?: string;
    role: string;
    siteId?: string;
  }): Observable<UserListDto> {
    return this.http.post<UserListDto>(API, request);
  }

  update(id: string, request: UpdateUserRequest): Observable<UserListDto> {
    return this.http.put<UserListDto>(`${API}/${id}`, request);
  }

  reInvite(id: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API}/${id}/re-invite`, {});
  }

  batchSetStatus(userIds: string[], isActive: boolean): Observable<void> {
    return this.http.post<void>(`${API}/batch-set-status`, { userIds, isActive });
  }
}
