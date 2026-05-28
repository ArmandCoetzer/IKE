import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, Subject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/notifications`;

/** Emits when unread count may have changed (e.g. after mark read). Layout can refresh. */
export const unreadCountChanged$ = new Subject<void>();

export interface NotificationDto {
  id: string;
  title: string;
  body: string;
  type: string;
  relatedEntityId?: string;
  createdAt: string;
  readAt?: string;
}

export interface UnreadCountDto {
  count: number;
}

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  constructor(private http: HttpClient) {}

  private requestOptions(skipGlobalLoader = false): { headers?: HttpHeaders } {
    if (!skipGlobalLoader) return {};
    return { headers: new HttpHeaders({ 'x-skip-loader': '1' }) };
  }

  getUnreadCount(skipGlobalLoader = false): Observable<UnreadCountDto> {
    return this.http.get<UnreadCountDto>(`${API}/unread-count`, this.requestOptions(skipGlobalLoader));
  }

  list(skipGlobalLoader = false): Observable<NotificationDto[]> {
    return this.http.get<NotificationDto[]>(API, this.requestOptions(skipGlobalLoader));
  }

  markRead(id: string): Observable<void> {
    return this.http.patch<void>(`${API}/${id}/read`, {}).pipe(
      tap(() => unreadCountChanged$.next())
    );
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${API}/mark-all-read`, {}).pipe(
      tap(() => unreadCountChanged$.next())
    );
  }

  /** Returns the route path for deep-linking to the related entity. */
  getLinkFor(n: NotificationDto): string[] {
    if (n.type === 'ServiceRequest' && n.relatedEntityId) return ['/service-requests', n.relatedEntityId];
    if ((n.type === 'OverdueInvoice' || n.type === 'InvoiceSent') && n.relatedEntityId) return ['/invoices', n.relatedEntityId];
    if ((n.type === 'JobCard' || n.type === 'JobCompleted') && n.relatedEntityId) return ['/job-cards', n.relatedEntityId];
    return ['/notifications'];
  }
}
