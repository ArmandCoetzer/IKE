import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

export interface DashboardCountsDto {
  unprocessedRequests: number;
  ongoingJobCards: number;
  overdueInvoices: number;
  requestsWithoutJobCard: number;
  completedJobsWithoutInvoice: number;
  lowStockPartsCount?: number;
  completedJobsCount?: number;
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  constructor(private http: HttpClient) {}

  getCounts(): Observable<DashboardCountsDto> {
    return this.http.get<DashboardCountsDto>(`${API_BASE}/dashboard/counts`);
  }
}
