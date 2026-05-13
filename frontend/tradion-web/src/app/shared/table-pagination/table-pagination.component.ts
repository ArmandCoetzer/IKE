import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-table-pagination',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './table-pagination.component.html',
  styleUrl: './table-pagination.component.scss'
})
export class TablePaginationComponent {
  /** Total rows in the (filtered) dataset. */
  @Input({ required: true }) totalCount!: number;
  /** Current page (1-based). */
  @Input() page = 1;
  @Input() pageSize = 10;
  @Output() pageChange = new EventEmitter<number>();

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  get startItem(): number {
    if (this.totalCount === 0) return 0;
    return (this.page - 1) * this.pageSize + 1;
  }

  get endItem(): number {
    return Math.min(this.page * this.pageSize, this.totalCount);
  }

  get showPager(): boolean {
    return this.totalCount > this.pageSize;
  }

  prev(): void {
    if (this.page > 1) this.pageChange.emit(this.page - 1);
  }

  next(): void {
    if (this.page < this.totalPages) this.pageChange.emit(this.page + 1);
  }
}
