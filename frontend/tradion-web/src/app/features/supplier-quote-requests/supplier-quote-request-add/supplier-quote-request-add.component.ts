import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SupplierQuoteRequestsService } from '../../../core/services/supplier-quote-requests.service';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';

import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-supplier-quote-request-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './supplier-quote-request-add.component.html'
})
export class SupplierQuoteRequestAddComponent implements OnInit {
  supplierId: string | null = null;
  lineItems: Array<{ partId: string | null; requestedQuantity: number | null }> = [{ partId: null, requestedQuantity: 1 }];
  notes = '';
  returnTo: string | null = null;
  suppliers: SupplierDto[] = [];
  parts: PartDto[] = [];
  submitting = false;
  showDraftModal = false;
  draftSending = false;
  draftToEmail = '';
  draftSubject = '';
  draftBody = '';
  draftItems: { partId: string; partName: string; requestedQuantity: number }[] = [];
  error: string | null = null;
  success: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private service: SupplierQuoteRequestsService,
    private suppliersService: SuppliersService,
    private partsService: PartsService
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    this.supplierId = q['supplierId'] ?? null;
    this.returnTo = sanitizeInternalReturnTo(q['returnTo']);
    this.notes = q['notes'] ?? '';
    if (q['partId']) {
      this.lineItems[0].partId = q['partId'];
    }
    if (q['requestedQuantity']) {
      const qty = Number(q['requestedQuantity']);
      this.lineItems[0].requestedQuantity = Number.isFinite(qty) && qty > 0 ? qty : 1;
    }
    this.suppliersService.list().subscribe({ next: (list) => (this.suppliers = list) });
    this.partsService.list(false).subscribe({
      next: (list) => {
        this.parts = list;
        this.onSupplierChanged();
        this.onLinePartChanged(0);
      }
    });
  }

  get selectedSupplier(): SupplierDto | null {
    if (!this.supplierId) return null;
    return this.suppliers.find(s => s.id === this.supplierId) ?? null;
  }

  get filteredParts(): PartDto[] {
    if (!this.supplierId) return [];
    return this.parts.filter(p => {
      if (p.isLabour) return false;
      if (p.supplierId === this.supplierId) return true;
      return (p.supplierIds ?? []).includes(this.supplierId!);
    });
  }

  get canAddMoreItems(): boolean {
    const uniqueSelected = new Set(this.lineItems.map(li => li.partId).filter((v): v is string => !!v));
    return this.filteredParts.length > uniqueSelected.size;
  }

  availablePartsForRow(index: number): PartDto[] {
    const selectedByOthers = new Set(
      this.lineItems
        .map((li, i) => (i === index ? null : li.partId))
        .filter((v): v is string => !!v)
    );
    return this.filteredParts.filter(p => !selectedByOthers.has(p.id));
  }

  onSupplierChanged(): void {
    if (!this.supplierId) {
      this.lineItems = [{ partId: null, requestedQuantity: 1 }];
      return;
    }
    for (const row of this.lineItems) {
      if (!row.partId) continue;
      const stillValid = this.filteredParts.some(p => p.id === row.partId);
      if (!stillValid) row.partId = null;
    }
  }

  addLineItem(): void {
    if (!this.canAddMoreItems) return;
    this.lineItems.push({ partId: null, requestedQuantity: 1 });
  }

  removeLineItem(index: number): void {
    if (this.lineItems.length <= 1) {
      this.lineItems[0] = { partId: null, requestedQuantity: 1 };
      return;
    }
    this.lineItems.splice(index, 1);
  }

  onLinePartChanged(index: number): void {
    const row = this.lineItems[index];
    if (!row || !row.partId) return;
    const part = this.filteredParts.find(p => p.id === row.partId);
    if (!part) return;
    if (!row.requestedQuantity || row.requestedQuantity < 1) {
      const needed = (part.reorderLevel ?? 0) - (part.quantity ?? 0);
      if (needed > 0) row.requestedQuantity = needed;
      else row.requestedQuantity = 1;
    }
  }

  save(): void {
    this.error = null;
    this.success = null;
    if (!this.supplierId) {
      this.error = 'Supplier is required.';
      return;
    }
    const validItems = this.lineItems
      .filter(li => !!li.partId)
      .map(li => ({
        partId: li.partId!,
        requestedQuantity: li.requestedQuantity && li.requestedQuantity > 0 ? li.requestedQuantity : 1
      }));
    if (validItems.length === 0) {
      this.error = 'At least one part is required.';
      return;
    }
    this.submitting = true;
    this.service.buildDraft({
      supplierId: this.supplierId,
      items: validItems,
      notes: this.notes.trim() || undefined
    }).subscribe({
      next: (draft) => {
        this.submitting = false;
        this.draftToEmail = draft.toEmail || '';
        this.draftSubject = draft.subject || '';
        this.draftBody = draft.body || '';
        this.draftItems = (draft.items ?? []).map(i => ({
          partId: i.partId,
          partName: i.partName,
          requestedQuantity: i.requestedQuantity
        }));
        this.showDraftModal = true;
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to build supplier draft.';
        this.submitting = false;
      }
    });
  }

  cancelDraft(): void {
    if (this.draftSending) return;
    this.showDraftModal = false;
  }

  sendDraft(): void {
    if (this.draftSending || !this.supplierId || this.draftItems.length === 0) return;
    if (!this.draftToEmail.trim() || !this.draftSubject.trim() || !this.draftBody.trim()) {
      this.error = 'Supplier email, subject and body are required.';
      return;
    }
    this.error = null;
    this.draftSending = true;
    this.service.sendDraft({
      supplierId: this.supplierId,
      toEmail: this.draftToEmail.trim(),
      subject: this.draftSubject.trim(),
      body: this.draftBody,
      items: this.draftItems.map(i => ({
        partId: i.partId,
        requestedQuantity: i.requestedQuantity,
        notes: this.notes.trim() || undefined
      }))
    }).subscribe({
      next: () => {
        this.draftSending = false;
        this.showDraftModal = false;
        this.resetForm();
        this.success = 'Supplier request sent. You can create another one.';
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to send supplier draft.';
        this.draftSending = false;
      }
    });
  }

  private resetForm(): void {
    this.supplierId = null;
    this.lineItems = [{ partId: null, requestedQuantity: 1 }];
    this.notes = '';
    this.draftToEmail = '';
    this.draftSubject = '';
    this.draftBody = '';
    this.draftItems = [];
  }
}
