import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { QuotesService, QuoteDto } from '../../../core/services/quotes.service';
import { ClientsService, ClientDto } from '../../../core/services/clients.service';
import { AuthService } from '../../../core/services/auth.service';
import { TableComponent } from '../../../shared/table/table.component';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { DocumentsService } from '../../../core/services/documents.service';
import { TablePaginationComponent } from '../../../shared/table-pagination/table-pagination.component';
import { clampTablePage } from '../../../shared/table-pagination/clamp-table-page';

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
  page = 1;
  readonly pageSize = 10;

  constructor(
    private quotesService: QuotesService,
    private clientsService: ClientsService,
    private documentsService: DocumentsService,
    public auth: AuthService
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

  downloadQuotePdf(q: QuoteDto): void {
    if (!q.id || this.downloadingQuoteId) return;
    this.downloadingQuoteId = q.id;
    this.documentsService.getQuotePdf(q.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Quote-${q.quoteNumber ?? q.id}.pdf`;
        a.click();
        URL.revokeObjectURL(url);
        this.downloadingQuoteId = null;
      },
      error: () => {
        this.downloadingQuoteId = null;
      }
    });
  }
}
