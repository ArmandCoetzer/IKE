import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/dev/audit/errors`;

export interface AuditErrorEntryDto {
  id: string;
  userId?: string;
  method: string;
  path: string;
  statusCode: number;
  message: string;
  details?: string;
  traceId?: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class AuditErrorsService {
  constructor(private http: HttpClient) {}

  list(take = 300, skip = 0): Observable<AuditErrorEntryDto[]> {
    return this.http.get<AuditErrorEntryDto[]>(`${API}?take=${take}&skip=${skip}`);
  }
}
