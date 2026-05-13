import {
  Component,
  OnInit,
  OnDestroy,
  AfterViewInit,
  ViewChild,
  ElementRef,
  signal,
  computed
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import * as L from 'leaflet';
import { TrackingService, TechnicianLocationDto } from '../../core/services/tracking.service';
import { AuthService } from '../../core/services/auth.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

// Default: South Africa (Johannesburg area)
const DEFAULT_CENTER: L.LatLngTuple = [-26.2041, 28.0473];
const DEFAULT_ZOOM = 6;
const POLL_INTERVAL_MS = 12_000; // 12 seconds

function escapeHtmlAttr(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/'/g, '&#39;');
}

@Component({
  selector: 'app-tracking-map',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PageHeaderComponent],
  templateUrl: './tracking-map.component.html',
  styleUrl: './tracking-map.component.scss'
})
export class TrackingMapComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapContainer') mapContainer!: ElementRef<HTMLDivElement>;

  locations = signal<TechnicianLocationDto[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  maxAgeMinutes = signal(120);
  showSites = signal(true);
  selectedUserId = signal<string | null>(null);
  private map: L.Map | null = null;
  private markers: L.Marker[] = [];
  private siteMarkers: L.Marker[] = [];
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  selectedLocation = computed(() => {
    const uid = this.selectedUserId();
    if (!uid) return null;
    return this.locations().find(l => l.userId === uid) ?? null;
  });

  constructor(
    private trackingService: TrackingService,
    public auth: AuthService
  ) {}

  ngOnInit(): void {
    if (!this.auth.hasPermission('ViewJobCards')) {
      this.error.set('You do not have permission to view technician tracking.');
      this.loading.set(false);
      return;
    }
    this.fetchLocations();
    this.pollTimer = setInterval(() => this.fetchLocations(), POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.markers.forEach(m => m.remove());
    this.siteMarkers.forEach(m => m.remove());
    this.map?.remove();
  }

  ngAfterViewInit(): void {
    if (!this.auth.hasPermission('ViewJobCards')) return;
    this.initMap();
  }

  private initMap(): void {
    if (!this.mapContainer?.nativeElement || this.map) return;
    this.map = L.map(this.mapContainer.nativeElement, {
      center: DEFAULT_CENTER,
      zoom: DEFAULT_ZOOM,
      zoomControl: false
    });
    L.control.zoom({ position: 'topright' }).addTo(this.map);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap contributors'
    }).addTo(this.map);
    this.updateMarkers();
  }

  private fetchLocations(): void {
    this.trackingService.getLocations({ maxAgeMinutes: this.maxAgeMinutes() }).subscribe({
      next: (list) => {
        this.locations.set(list);
        this.loading.set(false);
        this.error.set(null);
        this.updateMarkers();
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.message || 'Failed to load locations.');
      }
    });
  }

  private updateMarkers(): void {
    if (!this.map) return;

    this.markers.forEach(m => m.remove());
    this.markers = [];

    const list = this.locations();
    const bounds: L.LatLng[] = [];

    for (const loc of list) {
      const safeName = escapeHtmlAttr(loc.userName ?? '');
      const marker = L.marker([loc.latitude, loc.longitude], {
        icon: L.divIcon({
          className: 'technician-marker',
          html: `<span class="technician-marker-dot" title="${safeName}">👤</span>`,
          iconSize: [32, 32],
          iconAnchor: [16, 16]
        })
      });
      marker.bindTooltip(`${loc.userName ?? ''}${loc.jobCardNumber ? ` — ${loc.jobCardNumber}` : ''}`, {
        permanent: false,
        direction: 'top'
      });
      marker.on('click', () => this.selectedUserId.set(loc.userId));
      marker.addTo(this.map!);
      this.markers.push(marker);
      bounds.push(L.latLng(loc.latitude, loc.longitude));
    }

    // Site markers (if enabled and site has coordinates)
    this.siteMarkers.forEach(m => m.remove());
    this.siteMarkers = [];

    if (this.showSites()) {
      for (const loc of list) {
        if (loc.siteLatitude != null && loc.siteLongitude != null && loc.siteName) {
          const siteTitle = escapeHtmlAttr(loc.siteName);
          const marker = L.marker([loc.siteLatitude, loc.siteLongitude], {
            icon: L.divIcon({
              className: 'site-marker',
              html: `<span class="site-marker-dot" title="${siteTitle}">📍</span>`,
              iconSize: [28, 28],
              iconAnchor: [14, 14]
            })
          });
          marker.bindTooltip(`${loc.siteName}${loc.jobCardNumber ? ` — ${loc.jobCardNumber}` : ''}`, {
            permanent: false,
            direction: 'top'
          });
          marker.addTo(this.map!);
          this.siteMarkers.push(marker);
          bounds.push(L.latLng(loc.siteLatitude, loc.siteLongitude));
        }
      }
    }

    if (bounds.length === 1) {
      this.map!.setView(bounds[0], 14);
    } else if (bounds.length > 1) {
      this.map!.fitBounds(L.latLngBounds(bounds), { padding: [40, 40], maxZoom: 14 });
    }
  }

  refresh(): void {
    this.loading.set(true);
    this.fetchLocations();
  }

  selectTechnician(userId: string): void {
    this.selectedUserId.update(v => (v === userId ? null : userId));
    const loc = this.locations().find(l => l.userId === userId);
    if (loc && this.map) {
      this.map.setView([loc.latitude, loc.longitude], 14);
    }
  }

  toggleShowSites(): void {
    this.showSites.update(v => !v);
    this.updateMarkers();
  }

  changeMaxAge(): void {
    this.refresh();
  }

  formatTime(iso: string): string {
    const d = new Date(iso);
    const now = new Date();
    const diffMs = now.getTime() - d.getTime();
    const diffMins = Math.floor(diffMs / 60_000);
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    const diffHrs = Math.floor(diffMins / 60);
    return `${diffHrs}h ago`;
  }
}
