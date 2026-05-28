import { Component, HostListener, OnDestroy, OnInit, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { NotificationsService, NotificationDto, unreadCountChanged$ } from '../../core/services/notifications.service';
import { SignalRService } from '../../core/services/signalr.service';
import { BugLogsService } from '../../core/services/bug-logs.service';
import { SitesService } from '../../core/services/sites.service';
import { LoadingIndicatorService } from '../../core/services/loading-indicator.service';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, FormsModule],
  templateUrl: './main-layout.component.html',
  styleUrl: './main-layout.component.scss'
})
export class MainLayoutComponent implements OnInit, OnDestroy {
  sidebarOpen = signal(false);
  notificationsOpen = signal(false);
  userMenuOpen = signal(false);
  sidebarWorkOpen = signal(false);
  sidebarFinanceOpen = signal(false);
  sidebarResourcesOpen = signal(false);
  sidebarReferenceOpen = signal(false);
  sidebarTrainingOpen = signal(false);
  unreadCount = signal(0);
  recentNotifications = signal<NotificationDto[]>([]);
  notificationsLoading = signal(false);
  bugModalOpen = signal(false);
  bugTitle = '';
  bugDescription = '';
  bugImages: File[] = [];
  bugSubmitting = false;
  bugError: string | null = null;
  bugSuccess: string | null = null;
  clientCompanyName = signal<string | null>(null);

  constructor(
    public auth: AuthService,
    private notificationsService: NotificationsService,
    private signalR: SignalRService,
    private bugLogsService: BugLogsService,
    private sitesService: SitesService,
    private router: Router,
    public loadingIndicator: LoadingIndicatorService
  ) {}

  private refreshInterval: ReturnType<typeof setInterval> | null = null;
  private unreadSub = unreadCountChanged$.subscribe(() => this.refreshUnreadCount(true));
  private notificationSub: { unsubscribe: () => void } | null = null;
  private routeSub: { unsubscribe: () => void } | null = null;

  ngOnInit(): void {
    this.refreshUnreadCount();
    this.loadClientTopbarLabel();
    this.syncSidebarToRoute(this.router.url);
    this.routeSub = this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd)
    ).subscribe(e => this.syncSidebarToRoute(e.urlAfterRedirects || e.url));
    this.refreshInterval = setInterval(() => this.refreshUnreadCount(), 60_000);
    this.notificationSub = this.signalR.notificationReceived$.subscribe(() => {
      this.refreshUnreadCount(true);
      if (this.notificationsOpen()) this.loadRecentNotifications(true);
    });
  }

  private loadClientTopbarLabel(): void {
    if ((this.auth.role() || '').toLowerCase() !== 'client') {
      this.clientCompanyName.set(null);
      return;
    }
    this.sitesService.list(undefined, true).subscribe({
      next: (sites) => {
        const firstClientName = sites.find(s => !!s.clientName?.trim())?.clientName?.trim() || null;
        this.clientCompanyName.set(firstClientName);
      },
      error: () => {
        this.clientCompanyName.set(null);
      }
    });
  }

  ngOnDestroy(): void {
    this.unreadSub?.unsubscribe();
    this.notificationSub?.unsubscribe();
    this.routeSub?.unsubscribe();
    if (this.refreshInterval) clearInterval(this.refreshInterval);
  }

  private syncSidebarToRoute(rawUrl: string): void {
    const url = (rawUrl || '').split('?')[0].toLowerCase();
    const isClient = (this.auth.role() || '').toLowerCase() === 'client';
    const isWork = url.startsWith('/start-new-job')
      || url.startsWith('/service-requests')
      || url.startsWith('/job-cards')
      || url.startsWith('/tracking')
      || (isClient && url.startsWith('/reports'));
    const isFinance = url.startsWith('/quotes')
      || url.startsWith('/purchase-orders')
      || url.startsWith('/invoices');
    const isResources = url.startsWith('/parts')
      || url.startsWith('/suppliers')
      || url.startsWith('/supplier-quote-requests');
    const isTraining = url.startsWith('/training')
      || url.startsWith('/training-setup');

    this.sidebarWorkOpen.set(isWork);
    this.sidebarFinanceOpen.set(isFinance);
    this.sidebarResourcesOpen.set(isResources);
    this.sidebarTrainingOpen.set(isTraining);
    this.sidebarReferenceOpen.set(false);
  }

  refreshUnreadCount(skipGlobalLoader = false): void {
    this.notificationsService.getUnreadCount(skipGlobalLoader).subscribe({
      next: (d) => this.unreadCount.set(d.count),
      error: () => {}
    });
  }

  getNotificationLink(n: NotificationDto): string[] {
    return this.notificationsService.getLinkFor(n);
  }

  onNotificationClick(n: NotificationDto): void {
    this.notificationsOpen.set(false);
    if (!n.readAt) {
      this.notificationsService.markRead(n.id).subscribe({
        next: () => {
          const updated = new Date().toISOString();
          this.recentNotifications.update(list =>
            list.map(x => x.id === n.id ? { ...x, readAt: updated } : x)
          );
          this.refreshUnreadCount();
        }
      });
    }
  }

  openNotificationsPanel(): void {
    this.notificationsOpen.update(v => !v);
    if (this.notificationsOpen()) this.loadRecentNotifications();
  }

  private loadRecentNotifications(skipGlobalLoader = false): void {
    this.notificationsLoading.set(true);
    this.notificationsService.list(skipGlobalLoader).subscribe({
      next: (list) => {
        this.recentNotifications.set(list.slice(0, 5));
        this.notificationsLoading.set(false);
        this.refreshUnreadCount();
      },
      error: () => this.notificationsLoading.set(false)
    });
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.topbar-dropdown')) return;
    this.notificationsOpen.set(false);
    this.userMenuOpen.set(false);
    // Sidebar accordion (Training/Finance) only closes when user clicks its heading again, not on outside click
  }

  toggleSidebar(): void {
    this.sidebarOpen.update(v => !v);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }

  toggleNotifications(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.userMenuOpen.set(false);
    this.openNotificationsPanel();
  }

  toggleUserMenu(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.notificationsOpen.set(false);
    this.userMenuOpen.update(v => !v);
  }

  toggleSidebarWork(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.sidebarFinanceOpen.set(false);
    this.sidebarResourcesOpen.set(false);
    this.sidebarReferenceOpen.set(false);
    this.sidebarTrainingOpen.set(false);
    this.sidebarWorkOpen.update(v => !v);
  }

  toggleSidebarFinance(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.sidebarWorkOpen.set(false);
    this.sidebarResourcesOpen.set(false);
    this.sidebarReferenceOpen.set(false);
    this.sidebarTrainingOpen.set(false);
    this.sidebarFinanceOpen.update(v => !v);
  }

  toggleSidebarResources(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.sidebarWorkOpen.set(false);
    this.sidebarFinanceOpen.set(false);
    this.sidebarReferenceOpen.set(false);
    this.sidebarTrainingOpen.set(false);
    this.sidebarResourcesOpen.update(v => !v);
  }

  toggleSidebarReference(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.sidebarWorkOpen.set(false);
    this.sidebarFinanceOpen.set(false);
    this.sidebarResourcesOpen.set(false);
    this.sidebarTrainingOpen.set(false);
    this.sidebarReferenceOpen.update(v => !v);
  }

  toggleSidebarTraining(e: Event): void {
    e.preventDefault();
    e.stopPropagation();
    this.sidebarWorkOpen.set(false);
    this.sidebarFinanceOpen.set(false);
    this.sidebarResourcesOpen.set(false);
    this.sidebarReferenceOpen.set(false);
    this.sidebarTrainingOpen.update(v => !v);
  }

  closeAllDropdowns(): void {
    this.notificationsOpen.set(false);
    this.userMenuOpen.set(false);
    this.sidebarWorkOpen.set(false);
    this.sidebarFinanceOpen.set(false);
    this.sidebarResourcesOpen.set(false);
    this.sidebarReferenceOpen.set(false);
    this.sidebarTrainingOpen.set(false);
  }

  openBugModal(): void {
    this.bugModalOpen.set(true);
    this.bugError = null;
    this.bugSuccess = null;
  }

  closeBugModal(): void {
    if (this.bugSubmitting) return;
    this.bugModalOpen.set(false);
  }

  onBugImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.bugImages = Array.from(input?.files ?? []);
  }

  submitBug(): void {
    this.bugError = null;
    this.bugSuccess = null;
    if (!this.bugDescription.trim()) {
      this.bugError = 'Description is required.';
      return;
    }
    this.bugSubmitting = true;
    this.bugLogsService.create(this.bugTitle || null, this.bugDescription, this.bugImages).subscribe({
      next: () => {
        this.bugSubmitting = false;
        this.bugSuccess = 'Bug logged successfully.';
        this.bugTitle = '';
        this.bugDescription = '';
        this.bugImages = [];
        setTimeout(() => this.bugModalOpen.set(false), 700);
      },
      error: (err) => {
        this.bugSubmitting = false;
        this.bugError = err?.error?.message || 'Failed to save bug log.';
      }
    });
  }
}
