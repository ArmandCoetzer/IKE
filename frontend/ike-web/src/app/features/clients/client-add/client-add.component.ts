import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { concatMap, from } from 'rxjs';
import { ClientsService, CreateClientRequest } from '../../../core/services/clients.service';
import { SitesService } from '../../../core/services/sites.service';
import { ToastService } from '../../../core/services/toast.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

interface NewSiteRow {
  name: string;
  address: string;
}

@Component({
  selector: 'app-client-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './client-add.component.html',
  styleUrl: './client-add.component.scss'
})
export class ClientAddComponent {
  companyName = '';
  contactName = '';
  phone = '';
  email = '';
  siteRows: NewSiteRow[] = [{ name: '', address: '' }];
  submitting = false;
  error: string | null = null;

  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private clientsService = inject(ClientsService);
  private sitesService = inject(SitesService);
  private toast = inject(ToastService);

  addSiteRow(): void {
    this.siteRows = [...this.siteRows, { name: '', address: '' }];
  }

  removeSiteRow(index: number): void {
    if (this.siteRows.length <= 1) return;
    this.siteRows = this.siteRows.filter((_, i) => i !== index);
  }

  private navigateAfterClient(clientId: string): void {
    const returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParams['returnTo']);
    if (returnTo) {
      this.router.navigateByUrl(
        returnTo +
          (returnTo.includes('?') ? '&' : '?') +
          'newClientId=' +
          encodeURIComponent(clientId)
      );
    } else {
      this.router.navigate(['/clients', clientId]);
    }
  }

  private sitesToCreate(): { name: string; address?: string }[] {
    return this.siteRows
      .map((r) => ({
        name: r.name.trim(),
        address: r.address.trim()
      }))
      .filter((r) => r.name.length > 0)
      .map((r) => ({ name: r.name, address: r.address || undefined }));
  }

  save(): void {
    this.error = null;
    if (!this.companyName.trim()) {
      this.error = 'Company name is required.';
      this.toast.error('Company name is required.');
      return;
    }
    const sites = this.sitesToCreate();
    if (sites.length === 0) {
      this.error = 'Add at least one site with a name.';
      this.toast.error(this.error);
      return;
    }
    const body: CreateClientRequest = {
      companyName: this.companyName.trim(),
      contactName: this.contactName.trim() || undefined,
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined
    };
    this.submitting = true;
    this.clientsService.create(body).subscribe({
      next: (client) => {
        if (client.portalUserCreated === false && client.portalMessage) {
          this.toast.success('Client company saved.');
          this.toast.info(client.portalMessage);
        } else {
          this.toast.success('Client saved.');
        }
        from(sites)
          .pipe(
            concatMap((s) =>
              this.sitesService.create({
                name: s.name,
                address: s.address,
                clientId: client.id
              })
            )
          )
          .subscribe({
            complete: () => {
              this.submitting = false;
              this.toast.success('Sites saved.');
              this.navigateAfterClient(client.id);
            },
            error: (err) => {
              this.submitting = false;
              const msg =
                err.error?.message ||
                'Company was saved, but one or more sites could not be created.';
              this.toast.error(msg);
              this.router.navigate(['/clients', client.id, 'edit']);
            }
          });
      },
      error: (err) => {
        this.submitting = false;
        const msg = err.error?.message || 'Failed to create client.';
        this.error = msg;
        this.toast.error(msg);
      }
    });
  }
}
