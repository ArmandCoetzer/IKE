import { Injectable, signal } from '@angular/core';

export interface ConfirmDialogOptions {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  confirmButtonClass?: string;
}

export interface ConfirmDialogState extends Required<ConfirmDialogOptions> {
  id: number;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private dialog = signal<ConfirmDialogState | null>(null);
  private nextId = 0;
  private resolver: ((confirmed: boolean) => void) | null = null;

  readonly current = this.dialog.asReadonly();

  confirm(options: ConfirmDialogOptions): Promise<boolean> {
    if (this.resolver) {
      this.resolver(false);
      this.resolver = null;
    }

    const id = ++this.nextId;
    this.dialog.set({
      id,
      title: options.title,
      message: options.message,
      confirmText: options.confirmText ?? 'Confirm',
      cancelText: options.cancelText ?? 'Cancel',
      confirmButtonClass: options.confirmButtonClass ?? 'btn-primary'
    });

    return new Promise<boolean>((resolve) => {
      this.resolver = resolve;
    });
  }

  respond(confirmed: boolean): void {
    const resolve = this.resolver;
    this.resolver = null;
    this.dialog.set(null);
    resolve?.(confirmed);
  }
}
