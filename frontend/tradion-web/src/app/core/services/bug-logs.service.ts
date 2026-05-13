import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/dev/bugs`;

export interface BugLogAttachmentDto {
  id: string;
  fileName: string;
  contentType?: string;
  createdAt: string;
}

export interface BugLogListItemDto {
  id: string;
  title?: string;
  description: string;
  createdAt: string;
  userId?: string;
  userName?: string;
  attachmentCount: number;
}

export interface BugLogDto {
  id: string;
  title?: string;
  description: string;
  createdAt: string;
  userId?: string;
  userName?: string;
  attachments: BugLogAttachmentDto[];
}

@Injectable({ providedIn: 'root' })
export class BugLogsService {
  constructor(private http: HttpClient) {}

  create(title: string | null, description: string, images: File[]): Observable<BugLogDto> {
    const form = new FormData();
    if (title?.trim()) form.append('title', title.trim());
    form.append('description', description.trim());
    images.forEach((f) => form.append('images', f));
    return this.http.post<BugLogDto>(API, form);
  }

  list(take = 300, skip = 0): Observable<BugLogListItemDto[]> {
    return this.http.get<BugLogListItemDto[]>(`${API}?take=${take}&skip=${skip}`);
  }

  get(id: string): Observable<BugLogDto> {
    return this.http.get<BugLogDto>(`${API}/${id}`);
  }

  getAttachmentFile(bugId: string, attachmentId: string): Observable<Blob> {
    return this.http.get(`${API}/${bugId}/attachments/${attachmentId}/file`, { responseType: 'blob' });
  }
}
