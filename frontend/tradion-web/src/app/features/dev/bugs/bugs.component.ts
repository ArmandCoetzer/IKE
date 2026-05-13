import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BugLogDto, BugLogListItemDto, BugLogsService } from '../../../core/services/bug-logs.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-dev-bugs',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './bugs.component.html'
})
export class BugsComponent implements OnInit, OnDestroy {
  loading = true;
  error: string | null = null;
  rows: BugLogListItemDto[] = [];
  page = 1;
  readonly pageSize = 10;
  selected: BugLogDto | null = null;
  showModal = false;
  loadingDetail = false;
  detailError: string | null = null;
  attachmentUrls: Record<string, string> = {};

  constructor(private bugLogsService: BugLogsService) {}

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    Object.values(this.attachmentUrls).forEach((u) => URL.revokeObjectURL(u));
  }

  load(): void {
    this.loading = true;
    this.error = null;
    this.bugLogsService.list(500, 0).subscribe({
      next: (rows) => {
        this.rows = rows;
        this.loading = false;
        this.page = clampTablePage(this.page, this.rows.length, this.pageSize);
      },
      error: (err) => {
        this.error = err?.error?.message || 'Failed to load bugs.';
        this.loading = false;
      }
    });
  }

  openDetail(id: string): void {
    this.showModal = true;
    this.selected = null;
    this.detailError = null;
    this.loadingDetail = true;
    this.bugLogsService.get(id).subscribe({
      next: (d) => {
        this.selected = d;
        this.loadingDetail = false;
        d.attachments.forEach((a) => {
          this.bugLogsService.getAttachmentFile(d.id, a.id).subscribe({
            next: (blob) => {
              this.attachmentUrls[a.id] = URL.createObjectURL(blob);
            }
          });
        });
      },
      error: (err) => {
        this.detailError = err?.error?.message || 'Failed to load bug detail.';
        this.loadingDetail = false;
      }
    });
  }

  closeModal(): void {
    this.showModal = false;
    this.selected = null;
    this.detailError = null;
  }
}
