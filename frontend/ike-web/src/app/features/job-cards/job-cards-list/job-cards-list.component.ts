import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { JobCardsService, JobCardListDto } from '../../../core/services/job-cards.service';
import { AuthService } from '../../../core/services/auth.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { TableComponent } from '../../../shared/table/table.component';
import { LoaderComponent } from '../../../shared/loader/loader.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

@Component({
  selector: 'app-job-cards-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, LoaderComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './job-cards-list.component.html',
  styleUrl: './job-cards-list.component.scss'
})
export class JobCardsListComponent implements OnInit, OnDestroy {
  items: JobCardListDto[] = [];
  total = 0;
  searchText = '';
  filterStatus = '';
  loading = true;
  page = 1;
  readonly pageSize = 10;

  private jobCardSub: { unsubscribe: () => void } | null = null;

  constructor(
    private jobCardsService: JobCardsService,
    public auth: AuthService,
    private route: ActivatedRoute,
    private signalR: SignalRService
  ) {}

  load(skipGlobalLoader = false): void {
    this.loading = true;
    this.jobCardsService.list({
      status: this.filterStatus || undefined,
      search: this.searchText?.trim() || undefined,
      page: 1,
      pageSize: 1000
    }, skipGlobalLoader).subscribe({
      next: (r) => {
        this.items = r.items;
        this.total = r.items.length;
        this.page = clampTablePage(this.page, this.total, this.pageSize);
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.total / this.pageSize));
  }

  onPageChange(next: number): void {
    this.page = next;
  }

  invoiceStatusLabel(job: JobCardListDto): string {
    const status = (job.status || '').trim().toLowerCase();
    const completed = status === 'completed' || status === 'done' || status === 'closed';
    return completed ? (job.invoiceStatus || 'Not created') : '-';
  }

  ngOnInit(): void {
    const status = this.route.snapshot.queryParamMap.get('status');
    if (status) this.filterStatus = status;
    this.load();
    this.jobCardSub = this.signalR.jobCardUpdated$.subscribe(() => this.load(true));
  }

  ngOnDestroy(): void {
    this.jobCardSub?.unsubscribe();
  }
}
