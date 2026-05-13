import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { JobCardsService, CreateJobCardRequest } from '../../../core/services/job-cards.service';
import { ServiceRequestsService, ServiceRequestDto } from '../../../core/services/service-requests.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-job-card-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './job-card-add.component.html',
  styleUrl: './job-card-add.component.scss'
})
export class JobCardAddComponent implements OnInit {
  serviceRequestId: string | null = null;
  siteId: string | null = null;
  requests: ServiceRequestDto[] = [];
  sites: SiteDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private jobCardsService: JobCardsService,
    private serviceRequestsService: ServiceRequestsService,
    private sitesService: SitesService
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['serviceRequestId']) this.serviceRequestId = q['serviceRequestId'];
    if (q['siteId']) this.siteId = q['siteId'];
    this.loading = true;
    this.sitesService.list(undefined, true).subscribe({
      next: (sites) => {
        this.sites = sites;
        this.serviceRequestsService.list(this.siteId ?? undefined).subscribe({
          next: (list) => {
            this.requests = list;
            if (this.serviceRequestId) {
              const sr = list.find(r => r.id === this.serviceRequestId);
              if (sr) this.siteId = sr.siteId;
            }
            this.loading = false;
          },
          error: () => (this.loading = false)
        });
      },
      error: () => (this.loading = false)
    });
  }

  onRequestSelect(): void {
    const sr = this.requests.find(r => r.id === this.serviceRequestId);
    if (sr) this.siteId = sr.siteId;
  }

  get selectedSiteName(): string {
    if (!this.siteId) return '';
    const s = this.sites.find(site => site.id === this.siteId);
    return s?.name ?? this.siteId;
  }

  save(): void {
    this.error = null;
    if (!this.siteId) {
      this.error = this.serviceRequestId ? 'Select a service request first (site is set from the request).' : 'Site is required.';
      return;
    }
    const body: CreateJobCardRequest = {
      siteId: this.siteId
    };
    if (this.serviceRequestId) body.serviceRequestId = this.serviceRequestId;
    this.submitting = true;
    this.jobCardsService.create(body).subscribe({
      next: (job) => {
        this.submitting = false;
        this.router.navigate(['/job-cards', job.id]);
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to create job card.';
      }
    });
  }
}
