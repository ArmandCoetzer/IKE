import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InvoicesService, InvoiceDto, InvoiceLineItemInput, UpdateInvoiceRequest } from '../../../core/services/invoices.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

interface EditableInvoiceLineItem {
  lineType: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  partId?: string;
}

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
  lineItems: EditableInvoiceLineItem[] = [];
  parts: PartDto[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private invoicesService: InvoicesService,
    private partsService: PartsService
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
        this.lineItems = (inv.lineItems ?? []).map(li => ({
          lineType: li.lineType || 'Labour',
          description: li.description || '',
          quantity: li.quantity ?? 0,
          unitPrice: li.unitPrice ?? 0,
          discountPercent: li.discountPercent ?? 0,
          partId: li.partId
        }));
        if (!inv.partsConfirmed) {
          this.partsService.list().subscribe({ next: (parts) => (this.parts = parts) });
        }
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load invoice.';
        this.loading = false;
      }
    });
  }

  partsForLineType(lineType: string): PartDto[] {
    const labour = lineType === 'Labour';
    return this.parts.filter(p => !!p.isLabour === labour);
  }

  stockDisplayLabel(part: PartDto): string {
    if (part.isLabour) return part.name;
    const available = part.availableQuantity ?? part.quantity ?? 0;
    const reserved = part.reservedForActiveJobsQuantity ?? 0;
    const suffix = reserved > 0 ? ` (${reserved} taken for active jobs)` : '';
    return `${part.name} (${available} in stock${suffix})`;
  }

  addLineItem(): void {
    this.lineItems.push({ lineType: 'Labour', description: '', quantity: 1, unitPrice: 0, discountPercent: 0 });
  }

  removeLineItem(index: number): void {
    this.lineItems.splice(index, 1);
  }

  onPartSelect(index: number, partId: string | null): void {
    const row = this.lineItems[index];
    if (!row) return;
    row.partId = partId ?? undefined;
    if (!partId) {
      row.description = row.lineType === 'Labour' ? 'Labour' : '';
      return;
    }
    const part = this.parts.find(p => p.id === partId);
    if (!part) return;
    row.description = part.name;
    if (part.unitPrice != null) row.unitPrice = part.unitPrice;
  }

  save(): void {
    if (!this.id || !this.dueDate) return;
    this.error = null;
    this.submitting = true;
    const body: UpdateInvoiceRequest = {
      dueDate: this.dueDate,
      notes: this.notes.trim() || undefined
    };
    if (!this.item?.partsConfirmed) {
      const lineItems: InvoiceLineItemInput[] = this.lineItems
        .filter(li => li.quantity > 0 && li.unitPrice >= 0)
        .map(li => ({
          lineType: li.lineType,
          description: li.description,
          quantity: li.quantity,
          unitPrice: li.unitPrice,
          discountPercent: li.discountPercent ?? 0,
          partId: li.partId
        }));
      body.lineItems = lineItems;
    }
    this.invoicesService.update(this.id, body).subscribe({
      next: () => this.router.navigate(['/invoices', this.id]),
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update invoice.';
      }
    });
  }
}
