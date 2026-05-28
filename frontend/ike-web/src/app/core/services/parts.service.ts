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
  reorderLevel: number;
  isLowStock: boolean;
  supplierId?: string;
  supplierName?: string;
  hasSupplierEmail?: boolean;
  supplierIds?: string[];
  supplierNames?: string[];
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
}
