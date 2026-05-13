import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/invite`;

export interface InviteInfo {
  type: 'client' | 'employee';
  email: string;
}

export interface CompleteInviteRequest {
  token: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  password: string;
  confirmPassword: string;
}

export interface CompleteInviteResponse {
  message: string;
}

@Injectable({ providedIn: 'root' })
export class InviteService {
  constructor(private http: HttpClient) {}

  getInviteInfo(token: string): Observable<InviteInfo> {
    return this.http.get<InviteInfo>(`${API}/invite-info`, { params: { token } });
  }

  completeInvite(request: CompleteInviteRequest): Observable<CompleteInviteResponse> {
    return this.http.post<CompleteInviteResponse>(`${API}/complete`, request);
  }
}
