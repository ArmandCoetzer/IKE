import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ServiceRequestsService } from '../../../core/services/service-requests.service';
import { SitesService, SiteDto } from '../../../core/services/sites.service';
import { AuthService } from '../../../core/services/auth.service';
import { ClientsService } from '../../../core/services/clients.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-service-request-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './service-request-add.component.html',
  styleUrl: './service-request-add.component.scss'
})
export class ServiceRequestAddComponent implements OnInit {
  siteId: string | null = null;
  description = '';
  priority = 1;
  optionalDueDate = '';
  selectedFiles: File[] = [];
  sites: SiteDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private serviceRequestsService: ServiceRequestsService,
    private sitesService: SitesService,
    private clientsService: ClientsService,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    if ((this.auth.role() || '').toLowerCase() !== 'client') {
      this.router.navigate(['/service-requests']);
      return;
    }
    const q = this.route.snapshot.queryParams;
    if (q['siteId']) this.siteId = q['siteId'];
    this.loading = true;
    const requestedClientId = typeof q['clientId'] === 'string' ? q['clientId'] : null;
    if (requestedClientId) {
      this.loadSitesForClient(requestedClientId);
      return;
    }
    if ((this.auth.role() || '').toLowerCase() === 'client') {
      this.clientsService.list(true).subscribe({
        next: (clients) => {
          const current = clients[0];
          if (!current?.id) {
            this.loading = false;
            this.sites = [];
            return;
          }
          this.loadSitesForClient(current.id);
        },
        error: (err) => {
          this.loading = false;
          this.error = err?.error?.message || 'Failed to load sites.';
        }
      });
      return;
    }
    this.loadSitesForClient();
  }

  private loadSitesForClient(clientId?: string): void {
    this.sitesService.list(clientId, true).subscribe({
      next: (list) => {
        this.sites = list;
        if (this.siteId && !this.sites.some((s) => s.id === this.siteId)) {
          this.sitesService.get(this.siteId).subscribe({
            next: (site) => {
              this.sites = [site, ...this.sites];
            }
          });
        }
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'Failed to load sites.';
      }
    });
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFiles = input.files ? Array.from(input.files) : [];
  }

  save(): void {
    this.error = null;
    if (!this.siteId) {
      this.error = 'Site is required.';
      return;
    }
    if (!this.description.trim()) {
      this.error = 'Description is required.';
      return;
    }
    this.submitting = true;
    this.serviceRequestsService
      .create({
        siteId: this.siteId,
        description: this.description.trim(),
        priority: this.priority,
        optionalDueDate: this.optionalDueDate ? this.optionalDueDate : undefined
      })
      .subscribe({
        next: (sr) => {
          if (this.selectedFiles.length > 0) {
            this.serviceRequestsService.uploadAttachments(sr.id, this.selectedFiles).subscribe({
              next: () => {
                this.submitting = false;
                this.router.navigate(['/service-requests']);
              },
              error: (err) => {
                this.submitting = false;
                this.error = err.error?.message || 'Request created but attachment upload failed.';
              }
            });
          } else {
            this.submitting = false;
            this.router.navigate(['/service-requests']);
          }
        },
        error: (err) => {
          this.submitting = false;
          this.error = err.error?.message || 'Failed to create request.';
        }
      });
  }
}
