import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ServiceRequestsService, ServiceRequestDto } from '../../../core/services/service-requests.service';
import { AuthService } from '../../../core/services/auth.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../../shared/breadcrumb/breadcrumb.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-service-request-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, BreadcrumbComponent, PageHeaderComponent],
  templateUrl: './service-request-detail.component.html',
  styleUrl: './service-request-detail.component.scss'
})
export class ServiceRequestDetailComponent implements OnInit {
  item: ServiceRequestDto | null = null;
  loading = true;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private serviceRequestsService: ServiceRequestsService,
    public auth: AuthService
  ) {}

  updatingStatus = false;
  statusError: string | null = null;

  get breadcrumbs(): BreadcrumbItem[] {
    return [
      { label: 'Work', path: '/service-requests' },
      { label: 'Requests', path: '/service-requests' },
      { label: this.item?.requestNumber ?? '…' }
    ];
  }

  setStatus(status: string): void {
    if (!this.item?.id) return;
    this.statusError = null;
    this.updatingStatus = true;
    this.serviceRequestsService.updateStatus(this.item.id, status).subscribe({
      next: (updated) => {
        this.item = updated;
        this.updatingStatus = false;
      },
      error: (err) => {
        this.statusError = err.error?.message || 'Failed to update status.';
        this.updatingStatus = false;
      }
    });
  }

  downloadAttachment(attachmentId: string): void {
    if (!this.item?.id) return;
    this.serviceRequestsService.getAttachmentFile(this.item.id, attachmentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        const att = this.item?.attachments?.find(x => x.id === attachmentId);
        a.download = att?.fileName ?? 'attachment';
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      }
    });
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.serviceRequestsService.get(id).subscribe({
      next: (r) => {
        this.item = r;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load service request.';
        this.loading = false;
      }
    });
  }
}
