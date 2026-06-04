import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/parts`;

export interface PartDto {
  id: string;
  name: string;
  description?: string;
  partNumber?: string;
  quantity: number;
  reservedForActiveJobsQuantity?: number;
  availableQuantity?: number;
  reorderLevel: number;
  isLowStock: boolean;
  supplierId?: string;
  supplierName?: string;
  hasSupplierEmail?: boolean;
  supplierIds: string[];
  supplierNames: string[];
  unit?: string;
  unitPrice?: number;
  isLabour?: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreatePartRequest {
  name: string;
  description?: string;
  partNumber?: string;
  quantity: number;
  reorderLevel: number;
  supplierId?: string;
  supplierIds?: string[];
  unit?: string;
  unitPrice?: number;
  isLabour?: boolean;
}

export interface UpdatePartRequest {
  name?: string;
  description?: string;
  partNumber?: string;
  quantity?: number;
  reorderLevel?: number;
  supplierId?: string | null;
  supplierIds?: string[];
  unit?: string;
  unitPrice?: number | null;
  isLabour?: boolean;
}

@Injectable({ providedIn: 'root' })
export class PartsService {
  constructor(private http: HttpClient) {}

  list(lowStockOnly?: boolean, forCompanyId?: string): Observable<PartDto[]> {
    const p = new URLSearchParams();
    if (lowStockOnly) p.set('lowStockOnly', 'true');
    if (forCompanyId) p.set('forCompanyId', forCompanyId);
    const q = p.toString();
    return this.http.get<PartDto[]>(q ? `${API}?${q}` : API);
  }

  get(id: string): Observable<PartDto> {
    return this.http.get<PartDto>(`${API}/${id}`);
  }

  create(req: CreatePartRequest, forCompanyId?: string): Observable<PartDto> {
    const url = forCompanyId ? `${API}?forCompanyId=${encodeURIComponent(forCompanyId)}` : API;
    return this.http.post<PartDto>(url, req);
  }

  update(id: string, req: UpdatePartRequest): Observable<PartDto> {
    return this.http.put<PartDto>(`${API}/${id}`, req);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${API}/${id}`);
  }

  downloadImportTemplate(): Observable<Blob> {
    return this.http.get(`${API}/import-template`, { responseType: 'blob' });
  }

  importPreview(file: File): Observable<PartImportResultDto> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<PartImportResultDto>(`${API}/import-preview`, form);
  }

  importCommit(rows: PartImportRowDto[]): Observable<PartImportResultDto> {
    return this.http.post<PartImportResultDto>(`${API}/import-commit`, { rows });
  }
}

export interface PartImportRowDto {
  rowNumber: number;
  name: string;
  description?: string;
  partNumber?: string;
  quantity: number;
  reorderLevel: number;
  unit?: string;
  unitPrice: number;
  isLabour: boolean;
  errors: string[];
  createdPartId?: string;
}

export interface PartImportResultDto {
  rows: PartImportRowDto[];
  failedRows: PartImportRowDto[];
  totalRows: number;
  successCount: number;
  failedCount: number;
}
