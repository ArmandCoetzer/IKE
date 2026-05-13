import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InvoicesService, InvoiceDto, UpdateInvoiceRequest } from '../../../core/services/invoices.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-invoice-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './invoice-edit.component.html',
  styleUrl: './invoice-edit.component.scss'
})
export class InvoiceEditComponent implements OnInit {
  id: string | null = null;
  item: InvoiceDto | null = null;
  dueDate = '';
  notes = '';
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invoicesService: InvoicesService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.loading = true;
    this.invoicesService.get(this.id).subscribe({
      next: (inv) => {
        this.item = inv;
        if (inv.status === 'Paid') {
          this.error = 'Paid invoices are locked and cannot be edited.';
          this.loading = false;
          return;
        }
        this.dueDate = inv.dueDate ? inv.dueDate.toString().slice(0, 10) : '';
        this.notes = inv.notes ?? '';
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load invoice.';
        this.loading = false;
      }
    });
  }

  save(): void {
    if (!this.id || !this.dueDate) return;
    this.error = null;
    this.submitting = true;
    const body: UpdateInvoiceRequest = {
      dueDate: this.dueDate,
      notes: this.notes.trim() || undefined
    };
    this.invoicesService.update(this.id, body).subscribe({
      next: () => this.router.navigate(['/invoices', this.id]),
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update invoice.';
      }
    });
  }
}
