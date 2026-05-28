import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ServiceRequestsService, ServiceRequestDto, UpdateServiceRequestRequest } from '../../../core/services/service-requests.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-service-request-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './service-request-edit.component.html',
  styleUrl: './service-request-edit.component.scss'
})
export class ServiceRequestEditComponent implements OnInit {
  id: string | null = null;
  item: ServiceRequestDto | null = null;
  siteId: string | null = null;
  description = '';
  priority = 1;
  optionalDueDate = '';
  penaltyFee: number | null = null;
  penaltyNote = '';
  sites: SiteDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private serviceRequestsService: ServiceRequestsService,
    private sitesService: SitesService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.loading = true;
    this.sitesService.list(undefined, true).subscribe({
      next: (list) => (this.sites = list),
      error: () => (this.loading = false)
    });
    this.serviceRequestsService.get(this.id).subscribe({
      next: (sr) => {
        this.item = sr;
        this.siteId = sr.siteId;
        this.description = sr.description ?? '';
        this.priority = sr.priority ?? 1;
        this.optionalDueDate = sr.optionalDueDate ? sr.optionalDueDate.toString().slice(0, 10) : '';
        this.penaltyFee = sr.penaltyFee ?? null;
        this.penaltyNote = sr.penaltyNote ?? '';
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load request.';
        this.loading = false;
      }
    });
  }

  save(): void {
    if (!this.id || !this.siteId) return;
    this.error = null;
    if (!this.description.trim()) {
      this.error = 'Description is required.';
      return;
    }
    this.submitting = true;
    const body: UpdateServiceRequestRequest = {
      siteId: this.siteId,
      description: this.description.trim(),
      priority: this.priority,
      optionalDueDate: this.optionalDueDate ? this.optionalDueDate : undefined,
      penaltyFee: this.penaltyFee,
      penaltyNote: this.penaltyNote.trim() || null
    };
    this.serviceRequestsService.update(this.id, body).subscribe({
      next: () => this.router.navigate(['/service-requests', this.id]),
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update request.';
      }
    });
  }
}
