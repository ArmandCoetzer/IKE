import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/suppliers`;

export interface SupplierDto {
  id: string;
  name: string;
  email?: string;
  websiteUrl?: string;
  phone?: string;
  contactPerson?: string;
}

export interface CreateSupplierRequest {
  name: string;
  email?: string;
  websiteUrl?: string;
  phone?: string;
  contactPerson?: string;
}

export interface UpdateSupplierRequest {
  name: string;
  email: string;
  websiteUrl?: string;
  phone?: string;
  contactPerson?: string;
}

@Injectable({ providedIn: 'root' })
export class SuppliersService {
  constructor(private http: HttpClient) {}

  list(): Observable<SupplierDto[]> {
    return this.http.get<SupplierDto[]>(API);
  }

  create(req: CreateSupplierRequest): Observable<SupplierDto> {
    return this.http.post<SupplierDto>(API, req);
  }

  update(id: string, req: UpdateSupplierRequest): Observable<SupplierDto> {
    return this.http.put<SupplierDto>(`${API}/${id}`, req);
  }
}
