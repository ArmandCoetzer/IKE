import { Component, inject } from '@angular/core';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [],
  template: `
    @if (toast.current(); as t) {
      <div
        class="toast-message toast-{{ t.type }}"
        role="alert"
        (mouseenter)="toast.pause(t.id)"
        (mouseleave)="toast.resume(t.id)"
        (click)="toast.dismiss(t.id)"
      >
        <div class="toast-content">{{ t.message }}</div>
        <div class="toast-progress" [style.width.%]="toast.progressPercent(t)"></div>
      </div>
    }
  `,
  styles: [`
    :host {
      position: fixed;
      top: 1rem;
      right: 1rem;
      z-index: 9999;
      max-width: 360px;
    }
    .toast-message {
      padding: 0.75rem 1rem;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      cursor: pointer;
      animation: toast-in 0.25s ease;
      overflow: hidden;
      position: relative;
    }
    .toast-content {
      position: relative;
      z-index: 1;
      padding-bottom: 0.2rem;
    }
    .toast-progress {
      position: absolute;
      left: 0;
      bottom: 0;
      height: 4px;
      background: rgba(255, 255, 255, 0.85);
      transition: width 0.1s linear;
    }
    .toast-success {
      background: #0d9488;
      color: #fff;
    }
    .toast-error {
      background: #dc3545;
      color: #fff;
    }
    .toast-info {
      background: #0d6efd;
      color: #fff;
    }
    @keyframes toast-in {
      from { opacity: 0; transform: translateX(1rem); }
      to { opacity: 1; transform: translateX(0); }
    }
  `]
})
export class ToastContainerComponent {
  readonly toast = inject(ToastService);
}
