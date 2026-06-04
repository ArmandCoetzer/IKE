import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, map, of } from 'rxjs';
import { QuotesService, QuoteDto } from '../../../core/services/quotes.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { DocumentsService } from '../../../core/services/documents.service';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-quotes-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, TableComponent, PageHeaderComponent, TablePaginationComponent],
  templateUrl: './quotes-list.component.html',
  styleUrl: './quotes-list.component.scss'
})
export class QuotesListComponent implements OnInit {
  items: QuoteDto[] = [];
  clients: ClientDto[] = [];
  searchText = '';
  filterClientId = '';
  filterStatus = '';
  loading = true;
  downloadingQuoteId: string | null = null;
  deletingQuoteId: string | null = null;
  selectedQuoteIds = new Set<string>();
  bulkDeleting = false;
  bulkDeleteFailures: string[] = [];
  page = 1;
  readonly pageSize = 10;

  constructor(
    private quotesService: QuotesService,
    private clientsService: ClientsService,
    private documentsService: DocumentsService,
    public auth: AuthService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  get filtered(): QuoteDto[] {
    const q = this.searchText?.toLowerCase().trim();
    const status = this.filterStatus?.trim().toLowerCase();
    const clientId = this.filterClientId?.trim();
    let list = this.items;
    if (clientId) list = list.filter(i => i.clientId === clientId);
    if (status) list = list.filter(i => (i.status ?? '').toLowerCase() === status);
    if (!q) return list;
    return list.filter(i =>
      (i.quoteNumber ?? '').toLowerCase().includes(q) ||
      (i.clientName ?? '').toLowerCase().includes(q) ||
      (i.description ?? '').toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.clientsService.list(true).subscribe({ next: (list) => (this.clients = list) });
    this.quotesService.list().subscribe({
      next: (list) => {
        this.items = list;
        this.loading = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
      },
      error: () => (this.loading = false)
    });
  }

  get pagedFiltered(): QuoteDto[] {
    return this.filtered.slice((this.page - 1) * this.pageSize, this.page * this.pageSize);
  }

  get allSelected(): boolean {
    return this.filtered.length > 0 && this.filtered.every(q => this.selectedQuoteIds.has(q.id));
  }

  get someSelected(): boolean {
    return this.selectedQuoteIds.size > 0;
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) this.filtered.forEach(q => this.selectedQuoteIds.add(q.id));
    else this.filtered.forEach(q => this.selectedQuoteIds.delete(q.id));
  }

  toggleSelect(id: string, checked: boolean): void {
    if (checked) this.selectedQuoteIds.add(id);
    else this.selectedQuoteIds.delete(id);
  }

  isSelected(id: string): boolean {
    return this.selectedQuoteIds.has(id);
  }

  downloadQuotePdf(q: QuoteDto): void {
    if (!q.id || this.downloadingQuoteId) return;
    this.downloadingQuoteId = q.id;
    const request = q.isUploaded
      ? this.quotesService.getUploadedFile(q.id)
      : this.documentsService.getQuotePdf(q.id);
    request.subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = q.isUploaded
          ? (q.uploadedFileName || `Uploaded-Quote-${q.quoteNumber ?? q.id}`)
          : `Quote-${q.quoteNumber ?? q.id}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
        this.downloadingQuoteId = null;
      },
      error: () => {
        this.downloadingQuoteId = null;
      }
    });
  }

  async deleteQuote(q: QuoteDto): Promise<void> {
    if (!q.id || this.deletingQuoteId) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete quote',
      message: `Delete quote "${q.quoteNumber}"? Safe links will be detached first and this cannot be undone.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.deletingQuoteId = q.id;
    this.quotesService.delete(q.id).subscribe({
      next: () => {
        this.items = this.items.filter(item => item.id !== q.id);
        this.selectedQuoteIds.delete(q.id);
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        this.deletingQuoteId = null;
        this.bulkDeleteFailures = [];
        this.toast.success('Quote deleted.');
      },
      error: (err) => {
        this.deletingQuoteId = null;
        this.toast.error(err.error?.message || 'Failed to delete quote.');
      }
    });
  }

  async deleteSelectedQuotes(): Promise<void> {
    const selected = this.items.filter(q => this.selectedQuoteIds.has(q.id));
    if (selected.length === 0 || this.bulkDeleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete selected quotes',
      message: `Delete ${selected.length} selected quote${selected.length === 1 ? '' : 's'}? Quotes linked to locked records will be skipped with an explanation.`,
      confirmText: 'Delete selected',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;

    this.bulkDeleting = true;
    this.bulkDeleteFailures = [];
    forkJoin(selected.map(quote =>
      this.quotesService.delete(quote.id).pipe(
        map(() => ({ id: quote.id, label: quote.quoteNumber, success: true, message: '' })),
        catchError(err => of({
          id: quote.id,
          label: quote.quoteNumber,
          success: false,
          message: err.error?.message || 'Delete was blocked.'
        }))
      )
    )).subscribe({
      next: (results) => {
        const deletedIds = new Set(results.filter(r => r.success).map(r => r.id));
        this.items = this.items.filter(q => !deletedIds.has(q.id));
        deletedIds.forEach(id => this.selectedQuoteIds.delete(id));
        this.bulkDeleteFailures = results
          .filter(r => !r.success)
          .map(r => `${r.label}: ${r.message}`);
        this.bulkDeleting = false;
        this.page = clampTablePage(this.page, this.filtered.length, this.pageSize);
        if (this.bulkDeleteFailures.length === 0) {
          this.selectedQuoteIds.clear();
          this.toast.success(`${deletedIds.size} quote${deletedIds.size === 1 ? '' : 's'} deleted.`);
        } else if (deletedIds.size > 0) {
          this.toast.info(`${deletedIds.size} deleted. ${this.bulkDeleteFailures.length} could not be deleted.`);
        } else {
          this.toast.error('No selected quotes could be deleted.');
        }
      },
      error: () => {
        this.bulkDeleting = false;
        this.toast.error('Failed to delete selected quotes.');
      }
    });
  }
}
