import { Component, HostListener, inject } from '@angular/core';
import { ConfirmDialogService } from '../../services/confirm-dialog.service';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  template: `
    @if (confirm.current(); as dialog) {
      <div class="modal d-block confirm-backdrop" tabindex="-1" role="dialog" aria-modal="true">
        <div class="modal-dialog" role="document">
          <div class="modal-content">
            <div class="modal-header">
              <h5 class="modal-title">{{ dialog.title }}</h5>
              <button type="button" class="btn-close" aria-label="Close" (click)="cancel()"></button>
            </div>
            <div class="modal-body">
              <p class="mb-0">{{ dialog.message }}</p>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-light border" (click)="cancel()">{{ dialog.cancelText }}</button>
              <button type="button" class="btn {{ dialog.confirmButtonClass }}" (click)="confirmDelete()">
                {{ dialog.confirmText }}
              </button>
            </div>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .confirm-backdrop {
      background: rgba(0, 0, 0, 0.5);
      z-index: 1080;
    }
  `]
})
export class ConfirmDialogComponent {
  readonly confirm = inject(ConfirmDialogService);

  @HostListener('document:keydown.escape')
  cancel(): void {
    if (this.confirm.current()) this.confirm.respond(false);
  }

  confirmDelete(): void {
    this.confirm.respond(true);
  }
}
