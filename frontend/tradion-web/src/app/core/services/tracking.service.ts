import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/tracking`;

export interface TechnicianLocationDto {
  userId: string;
  userName: string;
  jobCardId?: string;
  jobCardNumber?: string;
  siteName?: string;
  siteAddress?: string;
  siteLatitude?: number;
  siteLongitude?: number;
  latitude: number;
  longitude: number;
  reportedAt: string;
}

export interface ReportLocationRequest {
  latitude: number;
  longitude: number;
  jobCardId?: string;
  accuracyMeters?: number;
}

@Injectable({ providedIn: 'root' })
export class TrackingService {
  constructor(private http: HttpClient) {}

  /**
   * Fetch latest technician positions for the live tracking map.
   * Requires ViewJobCards permission.
   */
  getLocations(params?: {
    jobCardId?: string;
    userId?: string;
    maxAgeMinutes?: number;
  }): Observable<TechnicianLocationDto[]> {
    let httpParams = new HttpParams();
    if (params?.jobCardId) httpParams = httpParams.set('jobCardId', params.jobCardId);
    if (params?.userId) httpParams = httpParams.set('userId', params.userId);
    if (params?.maxAgeMinutes != null) httpParams = httpParams.set('maxAgeMinutes', params.maxAgeMinutes.toString());
    const query = httpParams.toString();
    return this.http.get<TechnicianLocationDto[]>(query ? `${API}/locations?${query}` : `${API}/locations`);
  }

  /**
   * Report technician GPS position (used by mobile app).
   */
  reportLocation(body: ReportLocationRequest): Observable<void> {
    return this.http.request<void>('post', `${API}/location`, { body });
  }
}
