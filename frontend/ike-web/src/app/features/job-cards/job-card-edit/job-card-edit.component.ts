import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { JobCardWorkService, JobCardWorkDto, PlannedPartDto } from '../../../core/services/job-card-work.service';
import { JobCardsService, PlannedPartRequest } from '../../../core/services/job-cards.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { isInvoicePaid } from '../../../core/status/invoice-status';
import { isJobDraftLike } from '../../../core/status/job-status';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

interface PlannedPartRow {
  partId: string;
  partName: string;
  quantity: number;
  stockQuantity: number;
  reorderLevel: number;
  isLowStock: boolean;
}

@Component({
  selector: 'app-job-card-edit',
  standalone: true,
  imports: [CommonModule, FormsModule, BreadcrumbComponent, PageHeaderComponent],
  templateUrl: './job-card-edit.component.html',
  styleUrl: './job-card-edit.component.scss'
})
export class JobCardEditComponent implements OnInit {
  id: string | null = null;
  jobCardNumber = '';
  loading = true;
  submitting = false;
  error: string | null = null;

  permitsRequired = false;
  partsRequired = false;
  parts: PartDto[] = [];
  plannedParts: PlannedPartRow[] = [];
  selectedPartId: string | null = null;
  selectedPartQty = 1;

  jobDescription = '';
  jobPriority = 3;
  jobDueDate = '';

  returnTo: string | null = null;
  jobCompanyId: string | null = null;
  jobStatus: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jobCardWorkService: JobCardWorkService,
    private jobCardsService: JobCardsService,
    private partsService: PartsService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    this.returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.jobCardWorkService.get(this.id).subscribe({
      next: (j) => {
        if (isInvoicePaid(j.invoiceStatus)) {
          this.router.navigateByUrl(sanitizeInternalReturnTo(this.returnTo) || `/job-cards/${this.id}`);
          return;
        }
        this.jobCardNumber = j.jobCardNumber ?? '';
        this.permitsRequired = !!j.permitsRequired;
        this.partsRequired = !!j.partsRequired;
        this.jobDescription = j.description ?? '';
        this.jobPriority = j.priority ?? 3;
        this.jobDueDate = j.dueDate ? j.dueDate.toString().slice(0, 10) : '';
        this.plannedParts = (j.plannedParts ?? []).map(this.toPlannedPartRow);
        this.jobCompanyId = j.companyId ?? null;
        this.jobStatus = j.status ?? null;
        this.partsService.list(false, j.companyId ?? undefined).subscribe({ next: (p) => (this.parts = p) });
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load job card.';
        this.loading = false;
      }
    });
  }

  private toPlannedPartRow(p: PlannedPartDto): PlannedPartRow {
    return {
      partId: p.partId,
      partName: p.partName,
      quantity: p.quantity,
      stockQuantity: p.stockQuantity ?? 0,
      reorderLevel: p.reorderLevel ?? 0,
      isLowStock: p.isLowStock ?? false
    };
  }

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Work', path: '/job-cards' },
      { label: 'Job cards', path: '/job-cards' },
      { label: this.jobCardNumber || '…', path: this.id ? `/job-cards/${this.id}` : undefined },
      { label: 'Edit setup', path: undefined }
    ];
  }

  get returnToForJobEdit(): string {
    if (!this.id) return '/job-cards';
    let url = `/job-cards/${this.id}/edit`;
    if (this.returnTo) url += '?returnTo=' + encodeURIComponent(this.returnTo);
    return url;
  }

  get addPartQueryParams(): { returnTo: string; forCompanyId?: string } {
    const p: { returnTo: string; forCompanyId?: string } = { returnTo: this.returnToForJobEdit };
    if (this.jobCompanyId) p.forCompanyId = this.jobCompanyId;
    return p;
  }

  addPlannedPart(): void {
    if (!this.selectedPartId || this.selectedPartQty < 1) return;
    const part = this.parts.find(p => p.id === this.selectedPartId);
    if (!part) return;
    if (this.plannedParts.some(p => p.partId === this.selectedPartId)) return;
    this.plannedParts.push({
      partId: part.id,
      partName: part.name,
      quantity: this.selectedPartQty,
      stockQuantity: part.quantity,
      reorderLevel: part.reorderLevel,
      isLowStock: part.quantity <= part.reorderLevel
    });
    this.selectedPartId = null;
    this.selectedPartQty = 1;
  }

  removePlannedPart(partId: string): void {
    this.plannedParts = this.plannedParts.filter(p => p.partId !== partId);
  }

  isPartAlreadyPlanned(partId: string): boolean {
    return this.plannedParts.some(p => p.partId === partId);
  }

  openDatePicker(event: Event): void {
    const target = event.target as HTMLInputElement & { showPicker?: () => void };
    target.showPicker?.();
  }

  save(): void {
    if (!this.id) return;
    this.error = null;
    if (this.partsRequired && this.plannedParts.length === 0) {
      this.error = 'Add at least one part when parts are required.';
      return;
    }
    const plannedPartsBody: PlannedPartRequest[] = this.plannedParts.map(p => ({
      partId: p.partId,
      quantity: p.quantity
    }));
    const isDraft = isJobDraftLike(this.jobStatus);
    const updateBody: Parameters<typeof this.jobCardsService.update>[1] = {
      description: this.jobDescription?.trim() || undefined,
      priority: this.jobPriority,
      dueDate: this.jobDueDate || null,
      permitsRequired: this.permitsRequired,
      partsRequired: this.partsRequired,
      plannedParts: this.partsRequired ? plannedPartsBody : []
    };
    if (isDraft) updateBody.status = 'Open';
    this.submitting = true;
    this.jobCardsService.update(this.id, updateBody).subscribe({
      next: () => {
        this.submitting = false;
        const back = sanitizeInternalReturnTo(this.returnTo);
        if (back) this.router.navigateByUrl(back);
        else this.router.navigate(['/job-cards', this.id]);
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to save.';
        this.submitting = false;
      }
    });
  }

  goBack(): void {
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/job-cards', this.id!]);
  }
}
