import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  message: string;
  type: ToastType;
  durationMs: number;
  remainingMs: number;
  paused: boolean;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private toast = signal<Toast | null>(null);
  private nextId = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private lastTickAt = 0;

  readonly current = this.toast.asReadonly();

  success(message: string): void {
    this.show(message, 'success');
  }

  error(message: string): void {
    this.show(message, 'error');
  }

  info(message: string): void {
    this.show(message, 'info');
  }

  private show(message: string, type: ToastType): void {
    this.stopTimer();
    const id = ++this.nextId;
    const durationMs = 5000;
    this.toast.set({ id, message, type, durationMs, remainingMs: durationMs, paused: false });
    this.startTimer(id);
  }

  dismiss(id: number): void {
    if (this.toast()?.id !== id) return;
    this.stopTimer();
    this.toast.set(null);
  }

  pause(id: number): void {
    const current = this.toast();
    if (!current || current.id !== id || current.paused) return;
    this.tick(id);
    this.stopTimer();
    this.toast.update(t => t && t.id === id ? { ...t, paused: true } : t);
  }

  resume(id: number): void {
    const current = this.toast();
    if (!current || current.id !== id || !current.paused) return;
    if (current.remainingMs <= 0) {
      this.dismiss(id);
      return;
    }
    this.toast.update(t => t && t.id === id ? { ...t, paused: false } : t);
    this.startTimer(id);
  }

  progressPercent(toast: Toast): number {
    return Math.max(0, Math.min(100, (toast.remainingMs / toast.durationMs) * 100));
  }

  private startTimer(id: number): void {
    this.stopTimer();
    this.lastTickAt = Date.now();
    this.timer = setInterval(() => this.tick(id), 100);
  }

  private tick(id: number): void {
    const now = Date.now();
    const elapsed = now - this.lastTickAt;
    this.lastTickAt = now;
    const current = this.toast();
    if (!current || current.id !== id || current.paused) return;
    const remainingMs = Math.max(0, current.remainingMs - elapsed);
    this.toast.set({ ...current, remainingMs });
    if (remainingMs <= 0) {
      this.dismiss(id);
    }
  }

  private stopTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
