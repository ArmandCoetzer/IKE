import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './table.component.html',
  styleUrl: './table.component.scss'
})
export class TableComponent {
  /** Striped rows for readability. Default: true. */
  @Input() striped = true;

  /** Compact row padding (table-sm). Default: false. */
  @Input() compact = false;
}
