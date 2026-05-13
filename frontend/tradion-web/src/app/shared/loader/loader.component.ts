import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-loader',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (inline) {
      <span class="spinner-border spinner-border-sm" [class.text-primary]="!variant" [class.text-white]="variant === 'white'" role="status" [attr.aria-hidden]="true"></span>
    } @else {
      <div class="d-flex justify-content-center align-items-center" [style.min-height.px]="minHeight">
        <div class="spinner-border text-primary" role="status" [attr.aria-label]="label || 'Loading'">
          <span class="visually-hidden">{{ label || 'Loading' }}</span>
        </div>
      </div>
    }
  `,
  styles: []
})
export class LoaderComponent {
  @Input() inline = false;
  @Input() minHeight = 120;
  @Input() label = 'Loading';
  @Input() variant: 'primary' | 'white' = 'primary';
}
