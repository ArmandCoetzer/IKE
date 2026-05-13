import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ClientsService, ClientDto, UpdateClientRequest } from '../../../core/services/clients.service';
import { SitesService, SiteDto, UpdateSiteRequest } from '../../../core/services/sites.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

interface ClientSiteRow {
  id: string;
  name: string;
  address: string;
  latitude?: number;
  longitude?: number;
  isActive: boolean;
  saving: boolean;
  error: string | null;
}

@Component({
  selector: 'app-client-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './client-edit.component.html',
  styleUrl: './client-edit.component.scss'
})
export class ClientEditComponent implements OnInit {
  id: string | null = null;
  companyName = '';
  contactName = '';
  phone = '';
  email = '';
  isActive = true;
  loading = true;
  submitting = false;
  error: string | null = null;

  siteRows: ClientSiteRow[] = [];
  newSiteName = '';
  newSiteAddress = '';
  newSiteSaving = false;
  newSiteError: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private clientsService: ClientsService,
    private sitesService: SitesService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    forkJoin({
      client: this.clientsService.get(this.id),
      sites: this.sitesService.list(this.id)
    }).subscribe({
      next: ({ client, sites }) => {
        this.applyClient(client);
        this.siteRows = sites.map((s) => this.toRow(s));
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load client.';
        this.loading = false;
      }
    });
  }

  private applyClient(c: ClientDto): void {
    this.companyName = c.companyName ?? '';
    this.contactName = c.contactName ?? '';
    this.phone = c.phone ?? '';
    this.email = c.email ?? '';
    this.isActive = c.isActive ?? true;
  }

  private toRow(s: SiteDto): ClientSiteRow {
    return {
      id: s.id,
      name: s.name ?? '',
      address: s.address ?? '',
      latitude: s.latitude,
      longitude: s.longitude,
      isActive: s.isActive ?? true,
      saving: false,
      error: null
    };
  }

  saveClient(): void {
    if (!this.id) return;
    this.error = null;
    if (!this.companyName.trim()) {
      this.error = 'Company name is required.';
      return;
    }
    const body: UpdateClientRequest = {
      companyName: this.companyName.trim(),
      contactName: this.contactName.trim() || undefined,
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined,
      isActive: this.isActive
    };
    this.submitting = true;
    this.clientsService.update(this.id, body).subscribe({
      next: (client) => {
        this.submitting = false;
        this.applyClient(client);
        this.router.navigate(['/clients', client.id]);
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update client.';
      }
    });
  }

  saveSiteRow(row: ClientSiteRow): void {
    if (!this.id) return;
    if (!row.name.trim()) {
      row.error = 'Site name is required.';
      return;
    }
    row.saving = true;
    row.error = null;
    const addr = row.address.trim();
    const payload: UpdateSiteRequest = {
      name: row.name.trim(),
      address: addr,
      clientId: this.id,
      isActive: row.isActive
    };
    if (addr) {
      payload.latitude = row.latitude;
      payload.longitude = row.longitude;
    }
    this.sitesService.update(row.id, payload).subscribe({
      next: (s) => {
        row.saving = false;
        row.latitude = s.latitude ?? undefined;
        row.longitude = s.longitude ?? undefined;
      },
      error: (err) => {
        row.saving = false;
        row.error = err.error?.message || 'Failed to save site.';
      }
    });
  }

  addNewSite(): void {
    if (!this.id) return;
    this.newSiteError = null;
    if (!this.newSiteName.trim()) {
      this.newSiteError = 'Site name is required.';
      return;
    }
    this.newSiteSaving = true;
    this.sitesService
      .create({
        name: this.newSiteName.trim(),
        address: this.newSiteAddress.trim() || undefined,
        clientId: this.id
      })
      .subscribe({
        next: (s) => {
          this.siteRows.push(this.toRow(s));
          this.newSiteName = '';
          this.newSiteAddress = '';
          this.newSiteSaving = false;
        },
        error: (err) => {
          this.newSiteSaving = false;
          this.newSiteError = err.error?.message || 'Failed to create site.';
        }
      });
  }
}
