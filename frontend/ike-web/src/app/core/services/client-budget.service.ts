import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/clientbudget`;

export interface ClientBudgetDto {
  id: string;
  companyId: string;
  thresholdAmount: number;
  spentAmount: number;
  currency: string;
  workPaused: boolean;
  pausedAt?: string;
  continuationApprovedAt?: string;
  progressPercent?: number;
}

@Injectable({ providedIn: 'root' })
export class ClientBudgetService {
  constructor(private http: HttpClient) {}

  getMyBudget(): Observable<ClientBudgetDto> {
    return this.http.get<ClientBudgetDto>(API);
  }

  getBudgetForCompany(companyId: string): Observable<ClientBudgetDto> {
    return this.http.get<ClientBudgetDto>(`${API}/for-company/${companyId}`);
  }

  approveContinuation(): Observable<ClientBudgetDto> {
    return this.http.post<ClientBudgetDto>(`${API}/approve-continuation`, {});
  }
}
