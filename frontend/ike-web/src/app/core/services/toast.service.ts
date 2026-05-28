import { Injectable, signal, computed } from '@angular/core';

export type ToastType = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  message: string;
  type: ToastType;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private toasts = signal<Toast[]>([]);
  private nextId = 0;
  private dismissTimer: ReturnType<typeof setTimeout> | null = null;

  readonly current = computed(() => {
    const list = this.toasts();
    return list.length > 0 ? list[list.length - 1] : null;
  });

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
    if (this.dismissTimer) {
      clearTimeout(this.dismissTimer);
      this.dismissTimer = null;
    }
    const id = ++this.nextId;
    this.toasts.update(list => [...list, { id, message, type }]);
    this.dismissTimer = setTimeout(() => this.dismiss(id), 4000);
  }

  dismiss(id: number): void {
    if (this.dismissTimer) {
      clearTimeout(this.dismissTimer);
      this.dismissTimer = null;
    }
    this.toasts.update(list => list.filter(t => t.id !== id));
  }
}
