import { Component, EventEmitter, Input, Output } from '@angular/core';
import { RouterLink } from '@angular/router';

/** `router` uses [backLink]; `emit` renders a button and fires (back) for history / returnTo flows. */
export type PageHeaderBackBehavior = 'router' | 'emit';

/**
 * Standard page title row: optional back link + title + projected actions (primary buttons, etc.).
 * Keeps spacing and typography aligned across features.
 */
@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss'
})
export class PageHeaderComponent {
  @Input({ required: true }) title = '';
  /** When set, renders a small secondary back control (same as list/detail “← Back”). */
  /** Router commands accepted by [routerLink] (string or link array). */
  @Input() backLink?: string | any[];
  @Input() backLabel = '← Back';
  @Input() backBehavior: PageHeaderBackBehavior = 'router';
  @Output() back = new EventEmitter<void>();

  onBackClick(): void {
    this.back.emit();
  }
}
