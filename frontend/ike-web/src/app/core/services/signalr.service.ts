import { Injectable, OnDestroy, effect } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { API_BASE } from '../api-url';
import { AuthService } from './auth.service';

const HUB_PATH = '/hubs/app';

/** Must match backend AppHub constants */
const HubEvents = {
  JobCardUpdated: 'JobCardUpdated',
  NotificationReceived: 'NotificationReceived',
} as const;

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub: signalR.HubConnection | null = null;
  private connecting = false;

  /** Emits job card ID when a job card was updated. */
  readonly jobCardUpdated$ = new Subject<string>();

  /** Emits when the current user received a new notification. */
  readonly notificationReceived$ = new Subject<void>();

  constructor(private auth: AuthService) {
    effect(() => {
      const loggedIn = this.auth.isLoggedIn();
      if (!loggedIn) {
        this.disconnect();
      } else {
        this.connect();
      }
    });
  }

  ngOnDestroy(): void {
    this.disconnect();
  }

  /** Connect to the hub. Call when user is logged in. */
  async connect(): Promise<void> {
    if (this.hub?.state === signalR.HubConnectionState.Connected) return;
    if (this.connecting) return;

    const token = this.auth.getToken();
    if (!token) return;

    const baseUrl = API_BASE.replace(/\/api$/, '');
    const url = `${baseUrl}${HUB_PATH}`;

    this.connecting = true;
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(url, { accessTokenFactory: () => this.auth.getToken() ?? '' })
      .withAutomaticReconnect()
      .build();

    this.hub.on(HubEvents.JobCardUpdated, (jobCardId: string) => {
      this.jobCardUpdated$.next(jobCardId);
    });

    this.hub.on(HubEvents.NotificationReceived, () => {
      this.notificationReceived$.next();
    });

    try {
      await this.hub.start();
    } catch (err) {
      console.warn('[SignalR] Failed to connect:', err);
    } finally {
      this.connecting = false;
    }
  }

  disconnect(): void {
    if (this.hub) {
      this.hub.stop().catch(() => {});
      this.hub = null;
    }
  }
}
