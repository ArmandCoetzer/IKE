import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/jobcardwork`;

export interface PlannedPartDto {
  id: string;
  partId: string;
  partName: string;
  quantity: number;
  stockQuantity: number;
  reorderLevel: number;
  isLowStock: boolean;
}

export interface JobCardWorkDto {
  id: string;
  jobCardNumber: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  firstPermitRequestedAt?: string;
  firstPermitApprovedAt?: string;
  firstSitePhotoAt?: string;
  description?: string;
  serviceRequestId?: string;
  serviceRequestNumber?: string;
  serviceRequestDescription?: string;
  quoteId?: string;
  quoteNumber?: string;
  quoteAmount?: number;
  quoteDescription?: string;
  /** Draft | Sent | Accepted | Cancelled */
  quoteStatus?: string;
  siteId: string;
  siteName?: string;
  companyId?: string;
  requiredBadgeIds?: string[];
  requiredBadgeNames?: string[];
  status: string;
  priority?: number;
  dueDate?: string;
  invoiceId?: string;
  invoiceNumber?: string;
  invoiceStatus?: string;
  linkedPOs?: { id: string; pONumber: string; clientPONumber?: string }[];
  documents: { id: string; documentType: string; signedAt?: string; signedByUserName?: string; notes?: string; purchaseOrderId?: string; purchaseOrderNumber?: string; filePath?: string }[];
  parts: { id: string; brand?: string; serialNumber?: string; description?: string; oldPartPhotoPath?: string; newPartPhotoPath?: string }[];
  permits: {
    id: string;
    permitNumber?: number;
    permitTypeId?: string;
    masterPermitId?: string | null;
    status: string;
    permitTemplateName?: string;
    requestedAt: string;
    approvedAt?: string;
    validFrom?: string;
    validTo?: string;
    isWorkAuthorisation?: boolean;
    hasClientSignOff?: boolean;
    paperPermitNumber?: string | null;
    paperClientSignedOffAt?: string | null;
    attachments: { id: string; fileName: string; uploadedAt: string }[];
  }[];
  incidentReports: { id: string; description: string; severity?: string; status?: string; resolution?: string; photoPaths?: string[]; createdAt: string }[];
  permitsRequired?: boolean;
  requiredPermitTypeId?: string;
  requiredPermitTypeName?: string;
  partsRequired?: boolean;
  plannedParts?: PlannedPartDto[];
  assignments?: { userId: string; userName?: string; assignedAt: string; isPermitManager?: boolean; badgeIds?: string[] }[];
  activePermitId?: string;
  activePermitName?: string;
  budget?: { thresholdAmount: number; spentAmount: number; currency: string; workPaused: boolean };
  blockedReason?: string;
  waExpiredStandstill?: boolean;
  paperPermitMode?: boolean;
  canActivatePaperPermitMode?: boolean;
  finalClientSignOffAt?: string;
  finalClientSignOffByName?: string;
  /** Client print name captured with the signature (optional). */
  finalClientSignerName?: string;
}

export interface JobCardAssignmentDto {
  userId: string;
  userName?: string;
  assignedAt: string;
  isPermitManager?: boolean;
  badgeIds?: string[];
}

export interface AssignableTechnicianDto {
  userId: string;
  userName?: string;
  badgeIds: string[];
}

export interface AssignableTechniciansResponse {
  technicians: AssignableTechnicianDto[];
  requiredBadgeIds: string[];
  requiredBadgeNames: string[];
}

@Injectable({ providedIn: 'root' })
export class JobCardWorkService {
  constructor(private http: HttpClient) {}

  private requestOptions(skipGlobalLoader = false): { headers?: HttpHeaders } {
    if (!skipGlobalLoader) return {};
    return { headers: new HttpHeaders({ 'x-skip-loader': '1' }) };
  }

  get(id: string, skipGlobalLoader = false): Observable<JobCardWorkDto> {
    return this.http.get<JobCardWorkDto>(`${API}/${id}`, this.requestOptions(skipGlobalLoader));
  }

  assignTechnician(jobCardId: string, userId: string, isPermitManager = false): Observable<JobCardAssignmentDto> {
    return this.http.post<JobCardAssignmentDto>(`${API}/${jobCardId}/assignments`, { userId, isPermitManager });
  }

  setPermitManager(jobCardId: string, userId: string, isPermitManager: boolean): Observable<JobCardAssignmentDto> {
    return this.http.patch<JobCardAssignmentDto>(`${API}/${jobCardId}/assignments/${encodeURIComponent(userId)}`, { isPermitManager });
  }

  unassignTechnician(jobCardId: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${API}/${jobCardId}/assignments/${encodeURIComponent(userId)}`);
  }

  getAssignableTechnicians(jobCardId: string): Observable<AssignableTechniciansResponse> {
    return this.http.get<AssignableTechniciansResponse>(`${API}/${jobCardId}/assignable-technicians`);
  }

  submitDocumentWithFile(jobCardId: string, documentType: string, file: File | null, notes?: string): Observable<{ id: string; documentType: string; signedAt: string; signedByUserName?: string; notes?: string; filePath?: string }> {
    const form = new FormData();
    form.append('documentType', documentType);
    if (notes) form.append('notes', notes);
    if (file) form.append('file', file);
    return this.http.post<{ id: string; documentType: string; signedAt: string; signedByUserName?: string; notes?: string; filePath?: string }>(`${API}/${jobCardId}/documents/upload`, form);
  }

  getDocumentFile(jobCardId: string, docId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${API}/${jobCardId}/documents/${docId}/file`, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  addPart(jobCardId: string, data: { brand: string; serialNumber?: string; description?: string; oldPartPhoto?: File; newPartPhoto?: File }): Observable<JobPartDto> {
    const form = new FormData();
    form.append('brand', data.brand);
    if (data.serialNumber) form.append('serialNumber', data.serialNumber);
    if (data.description) form.append('description', data.description);
    if (data.oldPartPhoto) form.append('oldPartPhoto', data.oldPartPhoto);
    if (data.newPartPhoto) form.append('newPartPhoto', data.newPartPhoto);
    return this.http.post<JobPartDto>(`${API}/${jobCardId}/parts`, form);
  }

  getPartPhoto(jobCardId: string, partId: string, kind: 'old' | 'new'): Observable<Blob> {
    return this.http.get(`${API}/${jobCardId}/parts/${partId}/photo?kind=${kind}`, { responseType: 'blob' });
  }

  createIncident(jobCardId: string, data: { description: string; severity?: string }): Observable<IncidentReportDto> {
    return this.http.post<IncidentReportDto>(`${API}/${jobCardId}/incidents`, {
      description: data.description,
      severity: data.severity || 'Medium'
    });
  }

  createIncidentWithPhotos(jobCardId: string, description: string, severity: string, photos?: File[]): Observable<IncidentReportDto> {
    const form = new FormData();
    form.append('description', description);
    form.append('severity', severity);
    if (photos?.length) {
      photos.forEach((f, i) => form.append('photos', f, f.name));
    }
    return this.http.post<IncidentReportDto>(`${API}/${jobCardId}/incidents/with-photos`, form);
  }

  addIncidentPhotos(jobCardId: string, incidentId: string, photos: File[]): Observable<IncidentReportDto> {
    const form = new FormData();
    photos.forEach((f) => form.append('photos', f, f.name));
    return this.http.post<IncidentReportDto>(`${API}/${jobCardId}/incidents/${incidentId}/photos`, form);
  }

  getIncidentPhoto(jobCardId: string, incidentId: string, photoIndex: number): Observable<Blob> {
    return this.http.get(`${API}/${jobCardId}/incidents/${incidentId}/photos/${photoIndex}`, { responseType: 'blob' });
  }

  updateIncident(jobCardId: string, incidentId: string, data: { status?: string; resolution?: string }): Observable<IncidentReportDto> {
    return this.http.patch<IncidentReportDto>(`${API}/${jobCardId}/incidents/${incidentId}`, data);
  }

  /** Hides existing permits in the UI (history kept); technicians continue in paper permit mode. */
  activatePaperPermitMode(jobCardId: string): Observable<void> {
    return this.http.post<void>(`${API}/${jobCardId}/paper-permit-mode`, { enable: true });
  }

  /** Uploads captured client signature image; required before job status may move to completed. */
  finalClientSignOff(jobCardId: string, file: File, signerName?: string): Observable<{ id: string; documentType: string; signedAt: string }> {
    const form = new FormData();
    form.append('file', file);
    const trimmed = signerName?.trim();
    if (trimmed) form.append('signerName', trimmed);
    return this.http.post<{ id: string; documentType: string; signedAt: string }>(`${API}/${jobCardId}/final-client-sign-off`, form);
  }
}

export interface IncidentReportDto {
  id: string;
  description: string;
  severity: string;
  status?: string;
  resolution?: string;
  photoPaths?: string[];
  createdAt: string;
}

export interface JobPartDto {
  id: string;
  brand: string;
  serialNumber?: string;
  description?: string;
  oldPartPhotoPath?: string;
  newPartPhotoPath?: string;
}
