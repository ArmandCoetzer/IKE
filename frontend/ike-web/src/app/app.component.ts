import { Component, OnInit, OnDestroy, HostListener } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router, RouterOutlet } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from './core/services/auth.service';
import { ToastContainerComponent } from './core/components/toast-container/toast-container.component';
import { LoadingIndicatorService } from './core/services/loading-indicator.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastContainerComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly idleTimeoutMs = 30 * 60 * 1000; // 30 minutes
  private idleTimer: ReturnType<typeof setTimeout> | null = null;
  private hiddenAtMs: number | null = null;
  private readonly visibilityHandler = () => this.handleVisibilityChange();
  private routerSub: Subscription | null = null;

  constructor(
    private auth: AuthService,
    private router: Router,
    private loadingIndicator: LoadingIndicatorService
  ) {}

  ngOnInit(): void {
    this.trackRouterLoading();

    if (this.auth.getToken() && !this.auth.isTokenExpired()) {
      this.auth.me().subscribe({
        error: () => this.auth.logout()
      });
      this.resetIdleCountdown();
    }

    // Activity-based session timeout (not random): countdown restarts on user activity.
    const activityEvents = ['click', 'keydown', 'mousemove', 'touchstart', 'scroll'];
    for (const ev of activityEvents)
      window.addEventListener(ev, this.onUserActivity, { passive: true });
    document.addEventListener('visibilitychange', this.visibilityHandler);
  }

  ngOnDestroy(): void {
    const activityEvents = ['click', 'keydown', 'mousemove', 'touchstart', 'scroll'];
    for (const ev of activityEvents)
      window.removeEventListener(ev, this.onUserActivity);
    document.removeEventListener('visibilitychange', this.visibilityHandler);
    this.routerSub?.unsubscribe();
    this.clearIdleTimer();
  }

  private trackRouterLoading(): void {
    this.routerSub = this.router.events.subscribe(event => {
      if (event instanceof NavigationStart) {
        this.loadingIndicator.begin();
        return;
      }

      if (event instanceof NavigationEnd || event instanceof NavigationCancel || event instanceof NavigationError) {
        this.loadingIndicator.end();
      }
    });
  }

  private readonly onUserActivity = (): void => {
    if (!this.auth.getToken()) return;
    if (this.auth.isTokenExpired()) {
      this.auth.logout();
      return;
    }
    this.resetIdleCountdown();
  };

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    this.openDatePickerFromTarget(event.target);
  }

  @HostListener('document:focusin', ['$event'])
  onDocumentFocus(event: FocusEvent): void {
    this.openDatePickerFromTarget(event.target);
  }

  private openDatePickerFromTarget(target: EventTarget | null): void {
    const input = target as (HTMLInputElement & { showPicker?: () => void }) | null;
    if (!input || input.tagName !== 'INPUT' || input.type !== 'date') return;
    input.showPicker?.();
  }

  private clearIdleTimer(): void {
    if (this.idleTimer) {
      clearTimeout(this.idleTimer);
      this.idleTimer = null;
    }
  }

  private resetIdleCountdown(): void {
    if (!this.auth.getToken()) return;
    this.clearIdleTimer();
    this.idleTimer = setTimeout(() => {
      if (this.auth.getToken()) this.auth.logout();
    }, this.idleTimeoutMs);
  }

  private handleVisibilityChange(): void {
    if (!this.auth.getToken()) return;
    if (document.visibilityState === 'hidden') {
      this.hiddenAtMs = Date.now();
      return;
    }

    if (this.hiddenAtMs != null) {
      const hiddenForMs = Date.now() - this.hiddenAtMs;
      this.hiddenAtMs = null;
      if (hiddenForMs >= this.idleTimeoutMs) {
        this.auth.logout();
        return;
      }
    }
    this.resetIdleCountdown();
  }
}
