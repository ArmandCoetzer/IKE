import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NotificationsService, NotificationDto } from '../../core/services/notifications.service';
import { SignalRService } from '../../core/services/signalr.service';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="container-fluid">
      <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mb-4">
        <h1 class="mb-0">Notifications</h1>
        @if (hasUnread) {
          <button type="button" class="btn btn-outline-secondary btn-sm" (click)="markAllRead()" [disabled]="markAllInProgress">
            Mark all as read
          </button>
        }
      </div>
      <div class="card border-0 shadow-sm rounded-3">
        <div class="card-body p-4">
          @if (loading) {
            <p class="text-muted mb-0">Loading…</p>
          } @else if (notifications.length === 0) {
            <p class="text-muted mb-1">No notifications.</p>
            <p class="text-muted small mb-0">New service requests, overdue invoices, or other updates, they’ll show here.</p>
          } @else {
            <div class="list-group list-group-flush">
              @for (n of notifications; track n.id) {
                <div class="list-group-item d-flex justify-content-between align-items-start py-3" [class.bg-light]="!n.readAt">
                  <div class="flex-grow-1">
                    <a [routerLink]="notificationsService.getLinkFor(n)" (click)="markReadIfUnread(n)" class="text-dark text-decoration-none fw-medium">{{ n.title }}</a>
                    <p class="mb-0 small text-muted mt-1">{{ n.body }}</p>
                    <span class="small text-muted">{{ n.createdAt | date:'short' }}</span>
                  </div>
                  @if (!n.readAt) {
                    <button type="button" class="btn btn-outline-primary btn-sm ms-2" (click)="markRead(n)">Mark read</button>
                  }
                </div>
              }
            </div>
          }
        </div>
      </div>
    </div>
  `
})
export class NotificationsComponent implements OnInit, OnDestroy {
  notifications: NotificationDto[] = [];
  loading = true;
  markAllInProgress = false;
  private notificationSub: { unsubscribe: () => void } | null = null;

  constructor(
    public notificationsService: NotificationsService,
    private signalR: SignalRService
  ) {}

  get hasUnread(): boolean {
    return this.notifications.some(n => !n.readAt);
  }

  ngOnInit(): void {
    this.load();
    this.notificationSub = this.signalR.notificationReceived$.subscribe(() => this.load(true));
  }

  ngOnDestroy(): void {
    this.notificationSub?.unsubscribe();
  }

  load(skipGlobalLoader = false): void {
    this.loading = true;
    this.notificationsService.list(skipGlobalLoader).subscribe({
      next: (list) => {
        this.notifications = list;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  markRead(n: NotificationDto): void {
    if (n.readAt) return;
    this.notificationsService.markRead(n.id).subscribe({
      next: () => {
        n.readAt = new Date().toISOString();
      }
    });
  }

  markReadIfUnread(n: NotificationDto): void {
    if (n.readAt) return;
    this.notificationsService.markRead(n.id).subscribe({
      next: () => {
        n.readAt = new Date().toISOString();
      }
    });
  }

  markAllRead(): void {
    if (!this.hasUnread) return;
    this.markAllInProgress = true;
    this.notificationsService.markAllRead().subscribe({
      next: () => {
        this.notifications.forEach(n => { n.readAt = n.readAt ?? new Date().toISOString(); });
        this.markAllInProgress = false;
      },
      error: () => {
        this.markAllInProgress = false;
      }
    });
  }
}
