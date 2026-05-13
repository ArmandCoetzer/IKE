import { Component, OnInit, OnDestroy, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { JobCardWorkService, JobCardWorkDto, PlannedPartDto } from '../../../core/services/job-card-work.service';

/** Document types for site photos (before/mid/after work) - aligned with Flutter app */
export const SITE_PHOTO_DOCUMENT_TYPES = ['BeforeWork', 'MidWork', 'AfterWork'] as const;

/** Stored with job documents; client signature image for final job completion (matches API). */
export const FINAL_CLIENT_SIGN_OFF_DOCUMENT_TYPE = 'FinalClientSignOff';

import { JobCardsService } from '../../../core/services/job-cards.service';
import { JobPermitsService } from '../../../core/services/job-permits.service';
import { DocumentsService } from '../../../core/services/documents.service';
import { AuthService } from '../../../core/services/auth.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { WorkAuthorizationsService } from '../../../core/services/work-authorizations.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import {
  SupplierQuoteEmailDraftDto,
  SupplierQuoteRequestsService
} from '../../../core/services/supplier-quote-requests.service';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import {
  isPermitActiveLike,
  isPermitCapturedLike,
  isPermitClosedLike,
  isPermitDraftLike,
  isPermitExpiredLike,
  isPermitRejectedOrCancelled as isPermitRejectedOrCancelledStatus,
  normalizePermitStatus
} from '../../../core/status/permit-status';
import {
  isJobCompletedLike as isJobCompletedLikeStatus,
  isJobDraftLike,
  isJobInProgressLike,
  isJobOpenLike,
  normalizeJobStatus
} from '../../../core/status/job-status';
import { isInvoicePaid, normalizeInvoiceStatus } from '../../../core/status/invoice-status';

@Component({
  selector: 'app-job-card-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, BreadcrumbComponent, PageHeaderComponent],
  templateUrl: './job-card-detail.component.html',
  styleUrl: './job-card-detail.component.scss'
})
export class JobCardDetailComponent implements OnInit, OnDestroy {
  readonly sitePhotoTypes = SITE_PHOTO_DOCUMENT_TYPES;
  job: JobCardWorkDto | null = null;

  isSitePhotoType(type: string): boolean {
    return (SITE_PHOTO_DOCUMENT_TYPES as readonly string[]).includes(type);
  }

  get preWorkDocuments(): { id: string; documentType: string; signedAt?: string; signedByUserName?: string; notes?: string; purchaseOrderId?: string; purchaseOrderNumber?: string; filePath?: string }[] {
    if (!this.job?.documents) return [];
    return this.job.documents.filter(
      d => !this.isSitePhotoType(d.documentType) && d.documentType !== FINAL_CLIENT_SIGN_OFF_DOCUMENT_TYPE
    );
  }

  get waExpiredStandstill(): boolean {
    return !!this.job?.waExpiredStandstill;
  }

  get isClientUser(): boolean {
    return (this.auth.role() || '').trim().toLowerCase() === 'client';
  }

  get canManageJobCard(): boolean {
    return !this.isClientUser && this.auth.hasPermission('AssignTechnicians') && !this.isPaidAndLocked;
  }

  get canInitiateCommercialActions(): boolean {
    return !this.isClientUser && this.auth.hasPermission('ViewReports') && !this.isPaidAndLocked;
  }

  get sortedPermits(): NonNullable<JobCardWorkDto['permits']> {
    const list = [...(this.job?.permits ?? [])];
    return list.sort((a, b) => (b.permitNumber ?? 0) - (a.permitNumber ?? 0));
  }

  get sitePhotoDocuments(): { id: string; documentType: string; signedAt?: string; signedByUserName?: string; notes?: string; purchaseOrderId?: string; purchaseOrderNumber?: string; filePath?: string }[] {
    if (!this.job?.documents) return [];
    return this.job.documents.filter(d => this.isSitePhotoType(d.documentType));
  }
  loading = true;
  error: string | null = null;

  completingSetup = false;
  completeSetupError: string | null = null;

  assignableTechnicians: { userId: string; userName?: string }[] = [];
  assignUserId = '';
  assignAsPermitManager = false;
  assigning = false;
  downloadingPdf = false;
  emailingJobCardPdf = false;
  jobCardEmailError: string | null = null;
  newDocType = 'PreWork';
  docFile: File | null = null;
  submittingDoc = false;
  docError: string | null = null;
  newPartBrand = '';
  newPartSerial = '';
  newPartDescription = '';
  oldPartPhotoFile: File | null = null;
  newPartPhotoFile: File | null = null;
  addingPart = false;
  partError: string | null = null;
  newIncidentDescription = '';
  newIncidentSeverity = 'Medium';
  newIncidentPhotos: File[] = [];
  addingIncident = false;
  incidentError: string | null = null;
  previewingWorkAuthId: string | null = null;
  downloadingWorkAuthId: string | null = null;
  emailingWorkAuthId: string | null = null;
  emailingChildPermitId: string | null = null;
  downloadingChildPermitPdfId: string | null = null;
  /** Set while POST /jobpermits replacement request is in flight for a permit row. */
  requestingPermitReplacementId: string | null = null;
  workAuthPreviewHtml: SafeHtml | null = null;
  workAuthPreviewTitle = '';
  workAuthPreviewError: string | null = null;
  resolvingIncidentId: string | null = null;
  resolvingResolution = '';
  resolvingIncident = false;
  readonly Math = Math;
  /** Collapsible detail panels; defaults set once per job id (see `applyAccordionDefaults`). */
  accDocsOpen = true;
  accPermitsOpen = true;
  accIncidentsOpen = true;
  accTimestampsOpen = true;
  private accordionInitializedJobId: string | null = null;
  private refreshInterval: ReturnType<typeof setInterval> | null = null;
  private jobCardSub: { unsubscribe: () => void } | null = null;
  private partsById = new Map<string, PartDto>();
  showSupplierDraftModal = false;
  loadingSupplierDrafts = false;
  sendingAllSupplierDrafts = false;
  supplierDraftError: string | null = null;
  supplierDraftSuccess: string | null = null;
  supplierDrafts: Array<SupplierQuoteEmailDraftDto & { open: boolean; sending: boolean; error?: string | null }> = [];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jobCardWorkService: JobCardWorkService,
    private jobCardsService: JobCardsService,
    private jobPermitsService: JobPermitsService,
    private documentsService: DocumentsService,
    private partsService: PartsService,
    public auth: AuthService,
    private signalR: SignalRService,
    private workAuthorizationsService: WorkAuthorizationsService,
    private supplierQuoteRequestsService: SupplierQuoteRequestsService,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.jobCardWorkService.get(id).subscribe({
      next: (j) => {
        this.job = j;
        this.loadPartMetadata(j.companyId);
        if (this.accordionInitializedJobId !== id) {
          this.accordionInitializedJobId = id;
          this.applyAccordionDefaults();
        }
        if (this.auth.hasPermission('AssignTechnicians')) {
          this.jobCardWorkService.getAssignableTechnicians(id).subscribe({ next: (res) => (this.assignableTechnicians = res.technicians) });
        }
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load job card.';
        this.loading = false;
      }
    });
    // Fallback polling while viewing; SignalR still provides immediate push updates.
    this.startAutoRefresh(id);
    this.jobCardSub = this.signalR.jobCardUpdated$.subscribe((updatedId) => {
      const current = (id || '').trim().toLowerCase();
      const incoming = (updatedId || '').trim().toLowerCase();
      if (!incoming || incoming === current) this.refreshJob(id, true);
    });
  }

  private refreshJob(id: string, skipGlobalLoader = false): void {
    this.jobCardWorkService.get(id, skipGlobalLoader).subscribe({
      next: (j) => {
        this.job = j;
        this.loadPartMetadata(j.companyId);
      }
    });
  }

  private loadPartMetadata(companyId?: string): void {
    this.partsService.list(false, companyId).subscribe({
      next: (parts) => {
        this.partsById = new Map(parts.map((p) => [p.id, p]));
      }
    });
  }

  /** Strip trailing ` #123` from permit template titles (API may include permit numbers). */
  formatPermitDisplayName(name?: string | null): string {
    if (!name) return '';
    return name.replace(/\s+#\d+\s*$/, '').trim();
  }

  /** Remove ` #n` segments from site-photo notes (e.g. "Permit: Hot Work #1 | …"). */
  formatDocumentNotes(notes?: string | null): string {
    if (!notes) return '';
    return notes.replace(/\s+#\d+\b/g, '').replace(/\s{2,}/g, ' ').trim();
  }

  private applyAccordionDefaults(): void {
    const j = this.job;
    if (!j) return;
    const docCount = (j.documents ?? []).length;
    this.accDocsOpen = docCount <= 3;
    this.accPermitsOpen = (j.permits?.length ?? 0) <= 2;
    this.accIncidentsOpen = (j.incidentReports?.length ?? 0) <= 3;
    this.accTimestampsOpen = this.keyTimestamps.length <= 3;
  }

  ngOnDestroy(): void {
    this.jobCardSub?.unsubscribe();
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
      this.refreshInterval = null;
    }
  }

  private startAutoRefresh(id: string): void {
    if (this.refreshInterval) return;
    this.refreshInterval = setInterval(() => {
      if (!this.job?.id) return;
      this.jobCardWorkService.get(id, true).subscribe({ next: (j) => (this.job = j) });
    }, 30000);
  }

  completeSetup(): void {
    const j = this.job;
    const id = j?.id;
    if (!id || !isJobDraftLike(j?.status)) return;
    this.completeSetupError = null;
    this.completingSetup = true;
    this.jobCardsService.update(id, { status: 'Open' }).subscribe({
      next: () => {
        this.completingSetup = false;
        this.jobCardWorkService.get(id).subscribe({
          next: (updated) => { this.job = updated; }
        });
      },
      error: (err) => {
        this.completeSetupError = err.error?.message || 'Failed to complete setup.';
        this.completingSetup = false;
      }
    });
  }

  updatingStatus = false;
  statusError: string | null = null;
  showBlockModal = false;
  blockReason = '';
  blocking = false;
  unblocking = false;

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Work', path: '/job-cards' },
      { label: 'Job cards', path: '/job-cards' },
      { label: this.job?.jobCardNumber ?? '…', path: undefined }
    ];
  }

  /** True when status is Open and all prerequisites are in place. */
  get canStartWork(): boolean {
    const j = this.job;
    if (!j || !isJobOpenLike(j.status)) return false;
    if (!j.quoteId || !(j.linkedPOs?.length)) return false;
    if (!!j.permitsRequired && !this.hasApprovedPermit) return false;
    if (this.plannedPartsInsufficientStock.length > 0) return false;
    return true;
  }

  /** True when ALL required permits are no longer blocking (approved/active, closed/done, or not expired while live). */
  get hasApprovedPermit(): boolean {
    const j = this.job;
    if (!j?.permitsRequired) return true;
    const permits = j.permits ?? [];
    if (permits.length === 0) return false;
    const isSatisfied = (p: { status?: string; validTo?: string }) => {
      if (isPermitClosedLike(p.status)) return true;
      if (isPermitActiveLike(p.status))
        return !p.validTo || new Date(p.validTo) >= new Date();
      return false;
    };
    return permits.every(isSatisfied);
  }

  isPermitExpired(p: { validTo?: string; status?: string }): boolean {
    if (isPermitExpiredLike(p.status)) return true;
    if (!p.validTo) return false;
    return new Date(p.validTo) < new Date();
  }

  isPermitRejectedOrCancelled(p: { status?: string }): boolean {
    return isPermitRejectedOrCancelledStatus(p.status);
  }

  /** Expired (by date or status) and still a live concern — not completed, not rejected. */
  permitIsLiveExpired(
    p: { validTo?: string; status?: string; isWorkAuthorisation?: boolean }
  ): boolean {
    if (this.isPermitDone(p) || this.isPermitRejectedOrCancelled(p)) return false;
    return this.isPermitExpired(p);
  }

  /**
   * A newer permit row of the same kind exists (higher permit number). After a replacement is requested,
   * the expired row stays read-only with no actions.
   */
  permitHasSuccessorRequest(p: {
    id: string;
    permitNumber?: number;
    permitTypeId?: string;
    permitTemplateName?: string;
    isWorkAuthorisation?: boolean;
  }): boolean {
    const permits = this.job?.permits ?? [];
    const myNum = p.permitNumber ?? 0;
    for (const q of permits) {
      if (q.id === p.id) continue;
      if (this.isPermitRejectedOrCancelled(q)) continue;
      if ((q.permitNumber ?? 0) <= myNum) continue;
      if (p.isWorkAuthorisation) {
        if (q.isWorkAuthorisation) return true;
      } else {
        const sameType =
          !!(p.permitTypeId && q.permitTypeId && p.permitTypeId === q.permitTypeId) ||
          (!p.permitTypeId &&
            !q.permitTypeId &&
            (p.permitTemplateName || '').trim() === (q.permitTemplateName || '').trim());
        if (sameType) return true;
      }
    }
    return false;
  }

  permitExpiredActionMode(
    p: { id: string; validTo?: string; status?: string; permitNumber?: number; permitTypeId?: string; permitTemplateName?: string; isWorkAuthorisation?: boolean }
  ): 'normal' | 'replacement-only' | 'readonly-superseded' {
    if (!this.permitIsLiveExpired(p)) return 'normal';
    if (this.permitHasSuccessorRequest(p)) return 'readonly-superseded';
    return 'replacement-only';
  }

  /** Current usable Work Authorisation for linking a new child permit (not expired, not done, not rejected). */
  effectiveWaMasterForChildRequests(): NonNullable<JobCardWorkDto['permits']>[0] | null {
    const list = [...(this.job?.permits ?? [])].sort((a, b) => (b.permitNumber ?? 0) - (a.permitNumber ?? 0));
    for (const q of list) {
      if (!q.isWorkAuthorisation) continue;
      if (this.isPermitRejectedOrCancelled(q)) continue;
      if (this.isPermitDone(q)) continue;
      if (this.isPermitExpired(q)) continue;
      return q;
    }
    return null;
  }

  get canUserRequestPermitReplacement(): boolean {
    const uid = this.auth.jwtUserId();
    if (!uid) return false;
    return (this.job?.assignments ?? []).some((a) => a.userId === uid && a.isPermitManager);
  }

  canRequestExpiredChildReplacement(p: { isWorkAuthorisation?: boolean; permitTypeId?: string }): boolean {
    if (p.isWorkAuthorisation) return false;
    if (!p.permitTypeId) return false;
    return this.effectiveWaMasterForChildRequests() != null;
  }

  requestReplacementPermit(p: {
    id: string;
    isWorkAuthorisation?: boolean;
    permitTypeId?: string;
    permitTemplateName?: string;
  }): void {
    const jobId = this.job?.id;
    if (!jobId) return;
    if (this.requestingPermitReplacementId) return;
    this.permitError = null;
    this.requestingPermitReplacementId = p.id;
    if (p.isWorkAuthorisation) {
      this.jobPermitsService.requestPermit(jobId).subscribe({
        next: () => {
          this.requestingPermitReplacementId = null;
          this.jobCardWorkService.get(jobId).subscribe({ next: (j) => (this.job = j) });
        },
        error: (err) => {
          this.permitError = err.error?.message || 'Failed to request a new Work Authorisation.';
          this.requestingPermitReplacementId = null;
        }
      });
      return;
    }
    const master = this.effectiveWaMasterForChildRequests();
    if (!master?.id || !p.permitTypeId) {
      this.permitError =
        'A valid Work Authorisation is required before requesting this permit again. Renew the master permit first.';
      this.requestingPermitReplacementId = null;
      return;
    }
    this.jobPermitsService
      .requestPermit(jobId, { permitTypeId: p.permitTypeId, masterPermitId: master.id })
      .subscribe({
        next: () => {
          this.requestingPermitReplacementId = null;
          this.jobCardWorkService.get(jobId).subscribe({ next: (j) => (this.job = j) });
        },
        error: (err) => {
          this.permitError = err.error?.message || 'Failed to request a replacement permit.';
          this.requestingPermitReplacementId = null;
        }
      });
  }

  /** True only when the permit is fully completed (matches API closed-like states). */
  isPermitDone(p: { status?: string }): boolean {
    return isPermitClosedLike(p.status);
  }

  /** Badge colours: success only for Closed/Done; Active/Approved are in-progress, not complete. */
  permitStatusBadgeClasses(p: { status?: string }): Record<string, boolean> {
    const s = normalizePermitStatus(p.status);
    if (isPermitClosedLike(s)) return { 'bg-success': true };
    if (isPermitExpiredLike(s)) return { 'bg-danger': true };
    if (isPermitRejectedOrCancelledStatus(s)) return { 'bg-dark': true };
    if (s === 'pending' || isPermitDraftLike(s) || isPermitCapturedLike(s))
      return { 'bg-warning': true, 'text-dark': true };
    if (isPermitActiveLike(s)) return { 'bg-primary': true };
    return { 'bg-secondary': true };
  }

  /** Countdown / expiring-soon only after client sign-off and while permit is live (not closed). */
  private permitClockEligible(p: {
    status?: string;
    validTo?: string;
    hasClientSignOff?: boolean;
  }): boolean {
    if (!p.hasClientSignOff || this.isPermitDone(p) || !p.validTo) return false;
    if (!isPermitActiveLike(p.status)) return false;
    return !this.isPermitExpired(p);
  }

  isPermitExpiringSoon(p: {
    validFrom?: string;
    validTo?: string;
    requestedAt?: string;
    approvedAt?: string;
    status?: string;
    hasClientSignOff?: boolean;
  }): boolean {
    if (!this.permitClockEligible(p) || !p.validTo) return false;
    const validTo = new Date(p.validTo).getTime();
    const now = Date.now();
    if (now >= validTo) return false;
    const startRaw = p.validFrom ?? p.approvedAt ?? p.requestedAt;
    const start = startRaw ? new Date(startRaw).getTime() : now;
    if (!(start < validTo)) return false;
    const totalMs = validTo - start;
    if (totalMs <= 0) return false;
    const remainingMs = validTo - now;
    return remainingMs > 0 && remainingMs * 100 <= totalMs * 15;
  }

  permitTimeLeft(p: { validTo?: string; status?: string; hasClientSignOff?: boolean }): string {
    if (!this.permitClockEligible(p) || !p.validTo) return '';
    const end = new Date(p.validTo).getTime();
    const now = Date.now();
    let ms = end - now;
    if (ms <= 0) return 'Expired';
    const day = 24 * 60 * 60 * 1000;
    const hour = 60 * 60 * 1000;
    const minute = 60 * 1000;
    const d = Math.floor(ms / day);
    ms -= d * day;
    const h = Math.floor(ms / hour);
    ms -= h * hour;
    const m = Math.floor(ms / minute);
    if (d > 0) return `${d}d ${h}h left`;
    if (h > 0) return `${h}h ${m}m left`;
    return `${m}m left`;
  }

  /** Parts where quantity needed > stock on hand. Blocks starting the job. */
  get plannedPartsInsufficientStock(): PlannedPartDto[] {
    const parts = this.job?.plannedParts ?? [];
    return parts.filter(p => p.quantity > p.stockQuantity);
  }

  plannedPartTopUpNeeded(p: PlannedPartDto): number | undefined {
    const needed = (p.quantity ?? 0) - (p.stockQuantity ?? 0);
    return needed > 0 ? needed : undefined;
  }

  partForPlannedPart(p: PlannedPartDto): PartDto | undefined {
    return this.partsById.get(p.partId);
  }

  canRequestSupplierStockForPlannedPart(p: PlannedPartDto): boolean {
    const part = this.partForPlannedPart(p);
    return !!part && !part.isLabour && !!part.supplierId && !!part.hasSupplierEmail;
  }

  plannedPartSupplierRequestBlockReason(p: PlannedPartDto): string {
    const part = this.partForPlannedPart(p);
    if (!part) return 'Part not found';
    if (part.isLabour) return 'Labour item';
    if (!part.supplierId) return 'No supplier linked';
    if (!part.hasSupplierEmail) return 'Supplier email missing';
    return 'Not requestable';
  }

  /**
   * Request quantity rule:
   * - Base on reorder amount.
   * - When required amount for the job is greater than reorder amount, keep multiplying reorder until strictly greater than required.
   * - Keep safe fallbacks for missing/zero reorder values.
   */
  plannedPartRequestQuantity(p: PlannedPartDto): number {
    const requiredForJob = Math.max(0, p.quantity ?? 0);
    const shortfall = Math.max(0, requiredForJob - (p.stockQuantity ?? 0));
    const reorder = Math.max(0, this.partForPlannedPart(p)?.reorderLevel ?? p.reorderLevel ?? 0);

    if (reorder <= 0) {
      return Math.max(1, shortfall || requiredForJob || 1);
    }

    let requestQty = reorder;
    if (requiredForJob > reorder) {
      while (requestQty <= requiredForJob) {
        requestQty += reorder;
      }
    }

    return Math.max(requestQty, shortfall || 1);
  }

  plannedPartSupplierRequestQueryParams(p: PlannedPartDto): Record<string, string | number> {
    const part = this.partForPlannedPart(p);
    const requestQty = this.plannedPartRequestQuantity(p);
    return {
      partId: p.partId,
      supplierId: part?.supplierId ?? '',
      jobCardId: this.job?.id ?? '',
      requestedQuantity: requestQty,
      notes: `Stock request for ${p.partName}: job needs ${p.quantity}, in stock ${p.stockQuantity}, reorder ${part?.reorderLevel ?? p.reorderLevel}, requesting ${requestQty}.`,
      returnTo: this.jobCardReturnUrl
    };
  }

  openSupplierDraftModal(): void {
    if (this.isClientUser) return;
    if (!this.job?.id || this.loadingSupplierDrafts) return;
    this.supplierDraftError = null;
    this.supplierDraftSuccess = null;
    this.loadingSupplierDrafts = true;
    this.showSupplierDraftModal = true;
    this.supplierQuoteRequestsService.buildJobCardEmailDrafts(this.job.id).subscribe({
      next: (resp) => {
        this.supplierDrafts = (resp.drafts ?? []).map((d, i) => ({
          ...d,
          open: false,
          sending: false,
          error: null
        }));
        this.loadingSupplierDrafts = false;
      },
      error: (err) => {
        this.supplierDraftError = err.error?.message || 'Failed to build supplier email drafts.';
        this.loadingSupplierDrafts = false;
      }
    });
  }

  closeSupplierDraftModal(): void {
    if (this.sendingAllSupplierDrafts || this.supplierDrafts.some(d => d.sending)) return;
    this.showSupplierDraftModal = false;
  }

  toggleSupplierDraft(idx: number): void {
    const d = this.supplierDrafts[idx];
    if (!d) return;
    d.open = !d.open;
  }

  sendSupplierDraft(idx: number): void {
    const jobId = this.job?.id;
    const draft = this.supplierDrafts[idx];
    if (!jobId || !draft || draft.sending || !draft.toEmail?.trim() || !draft.subject?.trim() || !draft.body?.trim()) return;
    draft.error = null;
    draft.sending = true;
    this.supplierQuoteRequestsService.sendDraft({
      jobCardId: jobId,
      supplierId: draft.supplierId,
      toEmail: draft.toEmail.trim(),
      subject: draft.subject.trim(),
      body: draft.body,
      items: draft.items.map(i => ({
        partId: i.partId,
        requestedQuantity: i.requestedQuantity,
        notes: `Requested from job card ${this.job?.jobCardNumber ?? jobId}.`
      }))
    }).subscribe({
      next: () => {
        draft.sending = false;
        this.supplierDrafts.splice(idx, 1);
        this.supplierDraftSuccess = 'Supplier email sent and quote request records created.';
        if (this.supplierDrafts.length === 0) {
          this.showSupplierDraftModal = false;
        }
      },
      error: (err) => {
        draft.error = err.error?.message || 'Failed to send supplier email.';
        draft.sending = false;
      }
    });
  }

  sendAllSupplierDrafts(): void {
    if (this.supplierDrafts.length <= 1 || this.sendingAllSupplierDrafts) return;
    this.sendingAllSupplierDrafts = true;
    const sendNext = () => {
      const draft = this.supplierDrafts[0];
      if (!draft) {
        this.sendingAllSupplierDrafts = false;
        this.supplierDraftSuccess = 'All supplier emails sent.';
        this.showSupplierDraftModal = false;
        return;
      }
      if (!draft.toEmail?.trim() || !draft.subject?.trim() || !draft.body?.trim()) {
        this.sendingAllSupplierDrafts = false;
        this.supplierDraftError = `Supplier "${draft.supplierName}" has missing email subject/body.`;
        return;
      }
      draft.error = null;
      draft.sending = true;
      this.supplierQuoteRequestsService.sendDraft({
        jobCardId: this.job!.id,
        supplierId: draft.supplierId,
        toEmail: draft.toEmail.trim(),
        subject: draft.subject.trim(),
        body: draft.body,
        items: draft.items.map(i => ({
          partId: i.partId,
          requestedQuantity: i.requestedQuantity,
          notes: `Requested from job card ${this.job?.jobCardNumber ?? this.job?.id}.`
        }))
      }).subscribe({
        next: () => {
          draft.sending = false;
          this.supplierDrafts = this.supplierDrafts.filter(x => x.supplierId !== draft.supplierId);
          sendNext();
        },
        error: (err) => {
          draft.error = err.error?.message || 'Failed to send supplier email.';
          draft.sending = false;
          this.sendingAllSupplierDrafts = false;
        }
      });
    };
    sendNext();
  }

  get anySupplierDraftSending(): boolean {
    return this.supplierDrafts.some((d) => d.sending);
  }

  get startWorkBlockers(): string[] {
    const j = this.job;
    if (!j) return [];
    const blockers: string[] = [];
    if (!j.quoteId) blockers.push('Add quote');
    if (!(j.linkedPOs?.length)) blockers.push('Add purchase order');
    if (!!j.permitsRequired && !this.hasApprovedPermit) blockers.push('Obtain approved permit');
    if (this.plannedPartsInsufficientStock.length > 0) blockers.push('Resolve insufficient parts');
    return blockers;
  }

  get insufficientStockOrderNotes(): string {
    const lines = this.plannedPartsInsufficientStock.map(p => `- ${p.partName}: need ${p.quantity}, have ${p.stockQuantity}`);
    return ['Stock top-up request for this job:', ...lines].join('\n');
  }

  get firstLinkedPO(): { id: string; pONumber: string; clientPONumber?: string } | null {
    const list = this.job?.linkedPOs;
    return list?.length ? list[0] ?? null : null;
  }

  /** Next action for the technician - what to do now. */
  get nextAction(): { label: string; link?: string[]; queryParams?: Record<string, string>; isButton?: boolean; action?: () => void } | null {
    const j = this.job;
    if (!j) return null;
    if (this.isClientUser) return null;
    const status = normalizeJobStatus(j.status);

    if (isJobDraftLike(status)) {
      return { label: 'Complete setup', isButton: true, action: () => this.completeSetup() };
    }
    if (!j.quoteId) {
      return {
        label: 'Add quote',
        link: ['/quotes/new'],
        queryParams: { jobCardId: j.id, siteId: j.siteId, clientId: j.companyId ?? '', returnTo: this.jobCardReturnUrl }
      };
    }
    if (!(j.linkedPOs?.length)) {
      return {
        label: 'Add purchase order',
        link: ['/purchase-orders/new'],
        queryParams: { jobCardId: j.id, quoteId: j.quoteId!, siteId: j.siteId, clientId: j.companyId ?? '', amount: String(j.quoteAmount ?? 0), returnTo: this.jobCardReturnUrl }
      };
    }
    if (!!j.permitsRequired && !this.hasApprovedPermit && !isJobCompletedLikeStatus(status)) {
      const hasPending = (j.permits?.length ?? 0) > 0;
      return hasPending
        ? { label: 'Permit pending — technicians will upload on site', isButton: false }
        : { label: 'Permits required — technicians will request on site', isButton: false };
    }
    if (isJobOpenLike(status)) {
      return { label: 'Technicians start work on site', isButton: false };
    }
    if (isJobInProgressLike(status)) {
      return { label: 'Technicians complete work on site', isButton: false };
    }
    if (!j.invoiceId && this.canInitiateCommercialActions) {
      if (j.quoteId && !this.quoteAcceptedOnJob) {
        return { label: 'Quote must be accepted by the client before invoicing', isButton: false };
      }
      return {
        label: 'Create invoice',
        link: ['/invoices/new'],
        queryParams: { jobCardId: j.id, siteId: j.siteId, returnTo: this.jobCardReturnUrl }
      };
    }
    // Job is wrapped up (invoice on file); nothing actionable — hide "Your next step" on the detail view.
    return null;
  }

  get jobCardReturnUrl(): string {
    return this.job?.id ? `/job-cards/${this.job.id}` : '/job-cards';
  }

  get returnTo(): string | null {
    return sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  goBack(): void {
    const url = this.returnTo || '/job-cards';
    this.router.navigateByUrl(url);
  }

  get requestSummary(): string {
    const j = this.job;
    if (!j?.serviceRequestNumber) return '';
    const desc = (j.serviceRequestDescription || '').trim();
    return desc ? `${j.serviceRequestNumber} · ${desc.length > 50 ? desc.slice(0, 47) + '…' : desc}` : j.serviceRequestNumber;
  }

  get quoteSummary(): string {
    const j = this.job;
    if (!j?.quoteNumber) return '';
    const amt = j.quoteAmount != null ? `ZAR ${j.quoteAmount.toLocaleString('en-ZA', { minimumFractionDigits: 2 })}` : '';
    const desc = (j.quoteDescription || '').trim();
    const part = desc ? (desc.length > 40 ? desc.slice(0, 37) + '…' : desc) : '';
    const st = (j.quoteStatus || '').trim();
    const stPart = st ? ` · ${st}` : '';
    return part ? `${j.quoteNumber} · ${amt} · ${part}${stPart}` : `${j.quoteNumber} · ${amt}${stPart}`;
  }

  /** Quote lifecycle on the job: main step is complete only after client acceptance. */
  get quoteAcceptedOnJob(): boolean {
    return (this.job?.quoteStatus || '').trim().toLowerCase() === 'accepted';
  }

  get quoteSentOrBeyond(): boolean {
    const s = (this.job?.quoteStatus || '').trim().toLowerCase();
    return s === 'sent' || s === 'accepted';
  }

  get poSummary(): string {
    const po = this.firstLinkedPO;
    const j = this.job;
    if (!po) return '';
    const poNum = po.pONumber ?? 'PO';
    if (po.clientPONumber) return `${poNum} · Client PO #${po.clientPONumber}`;
    return j?.quoteNumber ? `${poNum} (for quote ${j.quoteNumber})` : poNum;
  }

  /** Permits with status for visual progress. */
  get permitStatusList(): { name: string; status: string; approvedAt?: string }[] {
    const j = this.job;
    if (!j?.permits?.length) return [];
    return j.permits.map(p => ({
      name: this.formatPermitDisplayName(p.permitTemplateName) || 'Permit',
      status: (p.status || '').toLowerCase(),
      approvedAt: p.approvedAt
    }));
  }

  /** Site photos progress: BeforeWork (required), MidWork (optional), AfterWork (required). */
  get sitePhotosProgress(): { type: string; label: string; done: boolean; required: boolean }[] {
    const docs = this.job?.documents ?? [];
    const hasBefore = docs.some(d => this.isSitePhotoType(d.documentType) && d.documentType === 'BeforeWork');
    const hasMid = docs.some(d => this.isSitePhotoType(d.documentType) && d.documentType === 'MidWork');
    const hasAfter = docs.some(d => this.isSitePhotoType(d.documentType) && d.documentType === 'AfterWork');
    return [
      { type: 'BeforeWork', label: 'Before', done: hasBefore, required: true },
      { type: 'MidWork', label: 'Mid-work', done: hasMid, required: false },
      { type: 'AfterWork', label: 'After', done: hasAfter, required: true }
    ];
  }

  get hasBeforePhoto(): boolean {
    return this.sitePhotosProgress.some(s => s.type === 'BeforeWork' && s.done);
  }

  get hasAfterPhoto(): boolean {
    return this.sitePhotosProgress.some(s => s.type === 'AfterWork' && s.done);
  }

  /** Workflow steps for visual progress - matches the job lifecycle. */
  get workflowSteps(): {
    key: string;
    label: string;
    done: boolean;
    inProgress?: boolean;
    variant?: 'awaitingPayment';
    summary: string;
    subItems?: { label: string; done: boolean }[];
    action?: { label: string; link?: string[]; queryParams?: Record<string, string>; isButton?: boolean; action?: () => void };
  }[] {
    const j = this.job;
    if (!j) return [];
    const status = (j.status || '').toLowerCase();
    const steps: {
      key: string;
      label: string;
      done: boolean;
      inProgress?: boolean;
      variant?: 'awaitingPayment';
      summary: string;
      subItems?: { label: string; done: boolean }[];
      action?: { label: string; link?: string[]; queryParams?: Record<string, string>; isButton?: boolean; action?: () => void };
    }[] = [];

    const hasQuote = !!j.quoteId;
    steps.push({
      key: 'quote',
      label: 'Quote',
      done: hasQuote && this.quoteAcceptedOnJob,
      inProgress: hasQuote && !this.quoteAcceptedOnJob,
      summary: this.quoteSummary,
      subItems: hasQuote
        ? [
            { label: 'Sent to client', done: this.quoteSentOrBeyond },
            { label: 'Accepted by client', done: this.quoteAcceptedOnJob }
          ]
        : undefined,
      action: this.isClientUser
        ? hasQuote
          ? { label: 'View quote', link: ['/quotes', j.quoteId!], queryParams: { returnTo: this.jobCardReturnUrl } }
          : undefined
        : !j.quoteId
          ? { label: 'Add quote', link: ['/quotes/new'], queryParams: { jobCardId: j.id, siteId: j.siteId, clientId: j.companyId ?? '', returnTo: this.jobCardReturnUrl } }
          : { label: 'View', link: ['/quotes', j.quoteId!], queryParams: { returnTo: this.jobCardReturnUrl } }
    });
    steps.push({
      key: 'po',
      label: 'Purchase order',
      done: !!(j.linkedPOs?.length),
      summary: this.poSummary,
      action: this.isClientUser
        ? undefined
        : !(j.linkedPOs?.length)
          ? { label: 'Add PO', link: ['/purchase-orders/new'], queryParams: { jobCardId: j.id, quoteId: j.quoteId ?? '', siteId: j.siteId, clientId: j.companyId ?? '', amount: String(j.quoteAmount ?? 0), returnTo: this.jobCardReturnUrl } }
          : this.firstLinkedPO
            ? { label: 'View', link: ['/purchase-orders', this.firstLinkedPO.id], queryParams: { returnTo: this.jobCardReturnUrl } }
            : undefined
    });
    if (!this.isClientUser) {
      const insufficient = this.plannedPartsInsufficientStock;
      steps.push({
        key: 'stock',
        label: 'Stock readiness',
        done: insufficient.length === 0,
        inProgress: insufficient.length > 0,
        summary: insufficient.length === 0 ? 'Sufficient stock' : `${insufficient.length} item(s) need ordering`,
        action: insufficient.length > 0
          ? { label: 'Request supplier quote', isButton: true, action: () => this.openSupplierDraftModal() }
          : undefined
      });
    }
    steps.push({
      key: 'work',
      label: 'Work started',
      done: isJobCompletedLikeStatus(status) || isJobInProgressLike(status),
      inProgress: isJobInProgressLike(status),
      summary: isJobCompletedLikeStatus(status) ? 'Completed' : isJobInProgressLike(status) ? 'In progress' : '—'
    });
    const permitsDone = !j.permitsRequired || this.hasApprovedPermit;
    const permitMarkedDone = (st: string) => {
      return isPermitClosedLike(st);
    };
    const permitSubs = this.permitStatusList.length
      ? this.permitStatusList.map(p => ({
          label: `${p.name}: ${permitMarkedDone(p.status) ? '✓' : p.status}`,
          done: permitMarkedDone(p.status)
        }))
      : undefined;
    steps.push({
      key: 'permits',
      label: 'Permits',
      done: permitsDone,
      inProgress: !!j.permits?.length && !permitsDone,
      summary: permitsDone
        ? 'All approved'
        : j.permits?.length
          ? `${j.permits.filter(p => permitMarkedDone(p.status ?? '')).length}/${j.permits.length} done`
          : 'Pending',
      subItems: permitSubs,
      action: undefined
    });
    const sitePhotoSubs = this.sitePhotosProgress.map(s => ({ label: `${s.label}${s.required ? ' (req)' : ''}`, done: s.done }));
    steps.push({
      key: 'photos',
      label: 'Site photos',
      done: this.hasBeforePhoto && this.hasAfterPhoto,
      inProgress: (this.hasBeforePhoto || this.hasAfterPhoto) && !(this.hasBeforePhoto && this.hasAfterPhoto),
      summary: this.hasBeforePhoto && this.hasAfterPhoto ? 'Before & after uploaded' : this.hasBeforePhoto ? 'Before done' : this.hasAfterPhoto ? 'After done' : '—',
      subItems: sitePhotoSubs
    });
    steps.push({
      key: 'clientSignOff',
      label: 'Final client sign-off',
      done: this.hasCapturedFinalClientSignature,
      inProgress: isJobInProgressLike(status) && !this.hasCapturedFinalClientSignature,
      summary: this.hasCapturedFinalClientSignature
        ? j.finalClientSignerName
          ? `Signed · ${j.finalClientSignerName}`
          : j.finalClientSignOffByName
            ? `Captured · ${j.finalClientSignOffByName}`
            : 'Captured'
        : '—'
    });
    steps.push({
      key: 'completed',
      label: 'Job completed',
      done: isJobCompletedLikeStatus(status),
      summary: isJobCompletedLikeStatus(status) ? 'Ready for invoice' : '—'
    });
    const hasInvoice = !!j.invoiceId;
    /** API omits invoice status for technicians — no orange/paid distinction in that case. */
    const invoiceStatusKnown = j.invoiceStatus != null && String(j.invoiceStatus).trim().length > 0;
    const statusNorm = normalizeInvoiceStatus(j.invoiceStatus);
    const invoicePaid = hasInvoice && invoiceStatusKnown && isInvoicePaid(statusNorm);
    const invoiceAwaitingPayment = hasInvoice && invoiceStatusKnown && !isInvoicePaid(statusNorm);
    const invoiceDoneWithoutPaymentInfo = hasInvoice && !invoiceStatusKnown;
    steps.push({
      key: 'invoice',
      label: 'Invoice',
      done: invoicePaid || invoiceDoneWithoutPaymentInfo,
      inProgress: invoiceAwaitingPayment,
      variant: invoiceAwaitingPayment ? 'awaitingPayment' : undefined,
      summary: hasInvoice
        ? invoicePaid
          ? (j.invoiceNumber ? `${j.invoiceNumber} · Paid` : 'Paid')
          : invoiceAwaitingPayment
            ? `${j.invoiceNumber ?? '—'} · Awaiting payment`
            : (j.invoiceNumber ?? '—')
        : '—',
      action: this.isClientUser
        ? undefined
        : j.invoiceId
        ? { label: 'View', link: ['/invoices', j.invoiceId], queryParams: { returnTo: this.jobCardReturnUrl } }
        : isJobCompletedLikeStatus(status) && this.canInitiateCommercialActions
          ? { label: 'Create invoice', link: ['/invoices/new'], queryParams: { jobCardId: j.id, siteId: j.siteId, returnTo: this.jobCardReturnUrl } }
          : undefined
    });
    if (!this.isPaidAndLocked) {
      return steps;
    }
    return steps.map((s) => {
      if (!s.action || s.action.isButton) {
        return { ...s, action: undefined };
      }
      const link = s.action.link;
      const isView =
        s.action.label === 'View' &&
        !!link?.length &&
        !link.some((seg) => seg === 'new');
      return { ...s, action: isView ? s.action : undefined };
    });
  }

  get keyTimestamps(): { label: string; value?: string }[] {
    if (!this.job) return [];
    type Row = { label: string; value: string; t: number };
    const rows: Row[] = [];
    const push = (label: string, value?: string) => {
      if (!value) return;
      const d = new Date(value);
      const t = d.getTime();
      if (Number.isNaN(t)) return;
      rows.push({ label, value, t });
    };
    push('Job created', this.job.createdAt);
    push('Work started', this.job.startedAt);
    const permitsOrdered = [...(this.job.permits ?? [])].sort((a, b) => {
      const ta = a.requestedAt ? new Date(a.requestedAt).getTime() : 0;
      const tb = b.requestedAt ? new Date(b.requestedAt).getTime() : 0;
      return ta - tb;
    });
    for (const p of permitsOrdered) {
      const name = this.formatPermitDisplayName(p.permitTemplateName) || 'Permit';
      push(`${name} — requested`, p.requestedAt);
      push(`${name} — approved`, p.approvedAt);
    }
    const hasSitePhotoDetailRows = (this.job.documents ?? []).some(
      (d) => !!d.signedAt && this.isSitePhotoType(d.documentType)
    );
    if (!hasSitePhotoDetailRows) {
      push('First site photo uploaded', this.job.firstSitePhotoAt);
    }
    push('Final client sign-off', this.job.finalClientSignOffAt);
    push('Job completed', this.job.completedAt);
    for (const d of this.job.documents ?? []) {
      if (!d.signedAt) continue;
      if (this.isSitePhotoType(d.documentType)) {
        const noteRaw = d.notes?.trim();
        const note = noteRaw ? ` — ${this.formatDocumentNotes(noteRaw)}` : '';
        push(`${d.documentType} photo uploaded${note}`, d.signedAt);
      } else {
        push(`${d.documentType} document uploaded`, d.signedAt);
      }
    }
    rows.sort((a, b) => a.t - b.t);
    return rows.map(({ label, value }) => ({ label, value }));
  }

  get isPaidAndLocked(): boolean {
    return isInvoicePaid(this.job?.invoiceStatus);
  }

  get isJobCompletedLike(): boolean {
    return isJobCompletedLikeStatus(this.job?.status);
  }

  /** Block coordinator status dropdown to terminal states until technicians record final client sign-off. */
  get hasCapturedFinalClientSignature(): boolean {
    return (this.job?.documents ?? []).some(
      d =>
        (d.documentType || '').toLowerCase() === FINAL_CLIENT_SIGN_OFF_DOCUMENT_TYPE.toLowerCase() &&
        !!(d.filePath && String(d.filePath).trim())
    );
  }

  get terminalCompletionBlocked(): boolean {
    return isJobInProgressLike(this.job?.status) && !this.hasCapturedFinalClientSignature;
  }

  setStatus(status: string): void {
    if (this.isClientUser) return;
    if (!this.job?.id || status === (this.job?.status || '')) return;
    this.statusError = null;
    this.updatingStatus = true;
    this.jobCardsService.updateStatus(this.job.id, status).subscribe({
      next: () => {
        this.jobCardWorkService.get(this.job!.id).subscribe({
          next: (j) => { this.job = j; this.updatingStatus = false; },
          error: () => { this.updatingStatus = false; }
        });
      },
      error: (err) => {
        this.statusError = err.error?.message || 'Failed to update status.';
        this.updatingStatus = false;
      }
    });
  }

  startWork(): void {
    if (!this.canStartWork || this.updatingStatus) return;
    this.setStatus('In Progress');
  }

  markWorkComplete(): void {
    if (!this.job?.id || !isJobInProgressLike(this.job.status) || this.updatingStatus) return;
    if (!this.hasCapturedFinalClientSignature) return;
    this.setStatus('Completed');
  }

  block(): void {
    if (this.isClientUser) return;
    const id = this.job?.id;
    const reason = this.blockReason?.trim();
    if (!id || !reason || this.blocking) return;
    this.blocking = true;
    this.jobCardsService.block(id, reason).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.showBlockModal = false;
        this.blockReason = '';
        this.blocking = false;
      },
      error: (err) => {
        this.statusError = err.error?.message || 'Failed to block job.';
        this.blocking = false;
      }
    });
  }

  unblock(): void {
    if (this.isClientUser) return;
    const id = this.job?.id;
    if (!id || this.unblocking) return;
    this.unblocking = true;
    this.jobCardsService.unblock(id).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.unblocking = false;
      },
      error: () => { this.unblocking = false; }
    });
  }

  permitError: string | null = null;
  uploadingPermitId: string | null = null;
  activatingPaperMode = false;

  confirmActivatePaperMode(): void {
    const id = this.job?.id;
    if (!id || !this.job?.canActivatePaperPermitMode) return;
    if (
      !confirm(
        'Switch this job to paper permits? This resets permits: all existing permits are removed and a fresh Work Authorisation draft is created in paper mode. Continue?'
      )
    )
      return;
    this.activatingPaperMode = true;
    this.permitError = null;
    this.jobCardWorkService.activatePaperPermitMode(id).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({
          next: (j) => {
            this.job = j;
            this.activatingPaperMode = false;
          },
          error: () => {
            this.activatingPaperMode = false;
          }
        });
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to switch to paper permit mode.';
        this.activatingPaperMode = false;
      }
    });
  }

  onPermitFileSelected(permitId: string, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input?.files?.[0];
    if (!file || !this.job?.id) return;
    this.permitError = null;
    this.uploadingPermitId = permitId;
    this.jobPermitsService.uploadAttachment(permitId, file).subscribe({
      next: () => {
        this.jobCardWorkService.get(this.job!.id).subscribe({
          next: (j) => { this.job = j; this.uploadingPermitId = null; if (input) input.value = ''; },
          error: () => { this.uploadingPermitId = null; }
        });
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to upload permit.';
        this.uploadingPermitId = null;
      }
    });
  }

  openWorkAuthPreview(permitId: string, permitName?: string): void {
    this.workAuthPreviewError = null;
    this.previewingWorkAuthId = permitId;
    this.workAuthorizationsService.getDocumentHtml(permitId).subscribe({
      next: (html) => {
        const cleaned = this.sanitizer.sanitize(SecurityContext.HTML, html) ?? '';
        this.workAuthPreviewHtml = this.sanitizer.bypassSecurityTrustHtml(cleaned);
        this.workAuthPreviewTitle = permitName || 'Work Authorisation';
        this.previewingWorkAuthId = null;
      },
      error: (err) => {
        this.workAuthPreviewError = err.error?.message || 'Failed to load permit preview.';
        this.previewingWorkAuthId = null;
      }
    });
  }

  closeWorkAuthPreview(): void {
    this.workAuthPreviewHtml = null;
    this.workAuthPreviewTitle = '';
  }

  downloadWorkAuthPdf(permitId: string, permitName?: string): void {
    this.downloadingWorkAuthId = permitId;
    this.workAuthorizationsService.downloadDocumentPdf(permitId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${permitName || 'work-authorisation'}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
        this.downloadingWorkAuthId = null;
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to download work authorisation PDF.';
        this.downloadingWorkAuthId = null;
      }
    });
  }

  emailWorkAuthClient(permitId: string): void {
    this.emailingWorkAuthId = permitId;
    this.workAuthorizationsService.emailClient(permitId).subscribe({
      next: () => {
        this.emailingWorkAuthId = null;
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to email permit to client.';
        this.emailingWorkAuthId = null;
      }
    });
  }

  assignTechnician(): void {
    const uid = this.assignUserId;
    const id = this.job?.id;
    if (!uid || !id || this.assigning) return;
    this.assigning = true;
    this.assignUserId = '';
    const asPermitManager = this.assignAsPermitManager;
    this.assignAsPermitManager = false;
    this.jobCardWorkService.assignTechnician(id, uid, asPermitManager).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.assigning = false;
      },
      error: () => (this.assigning = false)
    });
  }

  setAsPermitManager(userId: string): void {
    const id = this.job?.id;
    if (!id || this.assigning) return;
    this.assigning = true;
    this.jobCardWorkService.setPermitManager(id, userId, true).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.assigning = false;
      },
      error: () => (this.assigning = false)
    });
  }

  onDocFileSelected(e: Event): void {
    const input = e.target as HTMLInputElement;
    const f = input?.files?.[0];
    this.docFile = f ?? null;
    if (f) this.submitDocumentWithFile();
  }

  submitDocumentNoFile(): void {
    this.docFile = null;
    this.submitDocumentWithFile();
  }

  submitDocumentWithFile(): void {
    const id = this.job?.id;
    if (!id || this.submittingDoc) return;
    this.docError = null;
    this.submittingDoc = true;
    this.jobCardWorkService.submitDocumentWithFile(id, this.newDocType, this.docFile).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.docFile = null;
        this.submittingDoc = false;
      },
      error: () => { this.docError = 'Failed to add document.'; this.submittingDoc = false; }
    });
  }

  private static fileNameFromContentDisposition(header: string | null): string | null {
    if (!header) return null;
    const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
    if (star?.[1]) {
      try {
        return decodeURIComponent(star[1].trim());
      } catch {
        return star[1].trim();
      }
    }
    const m = /filename="([^"]+)"|filename=([^;\s]+)/i.exec(header);
    const raw = (m?.[1] || m?.[2] || '').trim();
    return raw ? raw.replace(/^["']|["']$/g, '') : null;
  }

  private static mimeFromPath(filePath?: string | null): string | null {
    if (!filePath) return null;
    const lower = filePath.toLowerCase();
    if (lower.endsWith('.png')) return 'image/png';
    if (lower.endsWith('.jpg') || lower.endsWith('.jpeg')) return 'image/jpeg';
    if (lower.endsWith('.pdf')) return 'application/pdf';
    return null;
  }

  private static extensionForMime(mime: string): string {
    const base = mime.split(';')[0].trim().toLowerCase();
    if (base === 'image/png') return '.png';
    if (base === 'image/jpeg' || base === 'image/jpg') return '.jpg';
    if (base === 'application/pdf') return '.pdf';
    return '';
  }

  isJobDocumentImage(d: { filePath?: string }): boolean {
    const m = JobCardDetailComponent.mimeFromPath(d.filePath);
    return m === 'image/png' || m === 'image/jpeg';
  }

  viewDocumentFile(docId: string): void {
    const id = this.job?.id;
    if (!id) return;
    this.jobCardWorkService.getDocumentFile(id, docId).subscribe({
      next: (resp) => {
        const body = resp.body;
        if (!body) return;
        const headerType = resp.headers.get('content-type');
        const baseType = headerType?.split(';')[0].trim() || 'application/octet-stream';
        const typed =
          baseType !== 'application/octet-stream' ? new Blob([body], { type: baseType }) : body;
        const url = URL.createObjectURL(typed);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 120000);
      }
    });
  }

  downloadDocumentFile(docId: string): void {
    const id = this.job?.id;
    if (!id) return;
    this.jobCardWorkService.getDocumentFile(id, docId).subscribe({
      next: (resp) => {
        const body = resp.body;
        if (!body) return;
        const cdName = JobCardDetailComponent.fileNameFromContentDisposition(resp.headers.get('content-disposition'));
        const headerType = resp.headers.get('content-type') || '';
        const baseType = headerType.split(';')[0].trim();
        let name = cdName || `document-${docId}`;
        if (!/\.\w{2,5}$/i.test(name)) {
          const ext = JobCardDetailComponent.extensionForMime(baseType);
          if (ext) name += ext;
        }
        const url = URL.createObjectURL(body);
        const a = document.createElement('a');
        a.href = url;
        a.download = name;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      }
    });
  }

  /** True when the filename is used as loose client signature evidence (embedded in PDFs / email; not shown as a standalone download in the UI). */
  isPermitSignatureAttachment(fileName?: string | null): boolean {
    return (fileName || '').toLowerCase().includes('signature');
  }

  downloadChildPermitPdf(permitId: string, permitName?: string): void {
    this.downloadingChildPermitPdfId = permitId;
    this.jobPermitsService.getDocumentationPdf(permitId).subscribe({
      next: (blob) => {
        const safe = (permitName || 'permit').replace(/[^\w\- ]+/g, '').trim() || 'permit';
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${safe}-documentation.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
        this.downloadingChildPermitPdfId = null;
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to download permit PDF.';
        this.downloadingChildPermitPdfId = null;
      }
    });
  }

  emailChildPermitToClient(permitId: string): void {
    this.emailingChildPermitId = permitId;
    this.jobPermitsService.emailClient(permitId).subscribe({
      next: () => {
        this.emailingChildPermitId = null;
      },
      error: (err) => {
        this.permitError = err.error?.message || 'Failed to email permit to client.';
        this.emailingChildPermitId = null;
      }
    });
  }

  viewPermitAttachment(attachmentId: string, fileName: string): void {
    this.jobPermitsService.downloadAttachment(attachmentId, fileName).subscribe({
      next: (blob) => {
        const fromName = JobCardDetailComponent.mimeFromPath(fileName);
        const mime =
          fromName ||
          (blob.type && blob.type !== 'application/octet-stream' ? blob.type : null) ||
          'application/octet-stream';
        const typed =
          blob.type && blob.type !== 'application/octet-stream' ? blob : new Blob([blob], { type: mime });
        const url = URL.createObjectURL(typed);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 120000);
      }
    });
  }

  addIncident(): void {
    const id = this.job?.id;
    if (!id || !this.newIncidentDescription?.trim() || this.addingIncident) return;
    this.incidentError = null;
    this.addingIncident = true;
    const desc = this.newIncidentDescription.trim();
    const sev = this.newIncidentSeverity;
    const hasPhotos = this.newIncidentPhotos.length > 0;
    const obs = hasPhotos
      ? this.jobCardWorkService.createIncidentWithPhotos(id, desc, sev, this.newIncidentPhotos)
      : this.jobCardWorkService.createIncident(id, { description: desc, severity: sev });
    obs.subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.newIncidentDescription = '';
        this.newIncidentSeverity = 'Medium';
        this.newIncidentPhotos = [];
        this.addingIncident = false;
      },
      error: () => {
        this.incidentError = 'Failed to report incident.';
        this.addingIncident = false;
      }
    });
  }

  onIncidentPhotosSelected(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.newIncidentPhotos = Array.from(input?.files ?? []);
  }

  startResolveIncident(ir: { id: string }): void {
    this.resolvingIncidentId = ir.id;
    this.resolvingResolution = '';
  }

  cancelResolveIncident(): void {
    this.resolvingIncidentId = null;
    this.resolvingResolution = '';
  }

  resolveIncident(incidentId: string): void {
    const id = this.job?.id;
    if (!id || !this.resolvingResolution?.trim() || this.resolvingIncident) return;
    this.resolvingIncident = true;
    this.jobCardWorkService.updateIncident(id, incidentId, { status: 'Resolved', resolution: this.resolvingResolution.trim() }).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.resolvingIncidentId = null;
        this.resolvingResolution = '';
        this.resolvingIncident = false;
      },
      error: () => { this.resolvingIncident = false; }
    });
  }

  openIncidentPhoto(incidentId: string, photoIndex: number): void {
    const id = this.job?.id;
    if (!id) return;
    this.jobCardWorkService.getIncidentPhoto(id, incidentId, photoIndex).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      }
    });
  }

  setActivePermit(permitId: string): void {
    const id = this.job?.id;
    if (!id) return;
    const val = permitId && permitId !== '' ? permitId : null;
    this.jobCardsService.update(id, { activeJobPermitId: val ?? undefined }).subscribe({
      next: () => this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) })
    });
  }

  onOldPartPhotoSelected(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.oldPartPhotoFile = input?.files?.[0] ?? null;
  }

  onNewPartPhotoSelected(e: Event): void {
    const input = e.target as HTMLInputElement;
    this.newPartPhotoFile = input?.files?.[0] ?? null;
  }

  addPart(): void {
    const id = this.job?.id;
    if (!id || !this.newPartBrand.trim() || this.addingPart) return;
    this.partError = null;
    this.addingPart = true;
    this.jobCardWorkService.addPart(id, {
      brand: this.newPartBrand.trim(),
      serialNumber: this.newPartSerial.trim() || undefined,
      description: this.newPartDescription.trim() || undefined,
      oldPartPhoto: this.oldPartPhotoFile ?? undefined,
      newPartPhoto: this.newPartPhotoFile ?? undefined
    }).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.newPartBrand = '';
        this.newPartSerial = '';
        this.newPartDescription = '';
        this.oldPartPhotoFile = null;
        this.newPartPhotoFile = null;
        this.addingPart = false;
      },
      error: () => { this.partError = 'Failed to add part.'; this.addingPart = false; }
    });
  }

  viewPartPhoto(partId: string, kind: 'old' | 'new'): void {
    const id = this.job?.id;
    if (!id) return;
    this.jobCardWorkService.getPartPhoto(id, partId, kind).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      }
    });
  }

  downloadJobCardPdf(): void {
    const id = this.job?.id;
    if (!id || this.downloadingPdf) return;
    this.downloadingPdf = true;
    this.documentsService.getJobCardPdf(id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `JobCard-${this.job?.jobCardNumber ?? id}.pdf`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
        this.downloadingPdf = false;
      },
      error: () => (this.downloadingPdf = false)
    });
  }

  emailJobCardToClient(): void {
    const id = this.job?.id;
    if (!id || this.emailingJobCardPdf) return;
    this.jobCardEmailError = null;
    this.emailingJobCardPdf = true;
    this.documentsService.emailJobCardToClient(id).subscribe({
      next: () => {
        this.emailingJobCardPdf = false;
      },
      error: (err) => {
        this.jobCardEmailError = err.error?.message || 'Failed to send job card to client.';
        this.emailingJobCardPdf = false;
      }
    });
  }

  unassign(userId: string): void {
    const id = this.job?.id;
    if (!id || this.assigning) return;
    this.assigning = true;
    this.jobCardWorkService.unassignTechnician(id, userId).subscribe({
      next: () => {
        this.jobCardWorkService.get(id).subscribe({ next: (j) => (this.job = j) });
        this.assigning = false;
      },
      error: () => (this.assigning = false)
    });
  }

  downloadPermitAttachment(attachmentId: string, fileName: string): void {
    this.jobPermitsService.downloadAttachment(attachmentId, fileName).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'permit.pdf';
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      }
    });
  }
}
