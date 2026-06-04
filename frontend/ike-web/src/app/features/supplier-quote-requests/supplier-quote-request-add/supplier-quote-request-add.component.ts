import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { SupplierQuoteEmailDraftDto, SupplierQuoteRequestsService } from '../../../core/services/supplier-quote-requests.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';

import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

interface PartRequestRow {
  part: PartDto;
  selected: boolean;
  requestedQuantity: number;
}

@Component({
  selector: 'app-supplier-quote-request-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './supplier-quote-request-add.component.html'
})
export class SupplierQuoteRequestAddComponent implements OnInit {
  returnTo: string | null = null;
  parts: PartDto[] = [];
  partRows: PartRequestRow[] = [];
  submitting = false;
  showDraftModal = false;
  sendingAllDrafts = false;
  drafts: Array<SupplierQuoteEmailDraftDto & { open: boolean; sending: boolean; error?: string | null }> = [];
  error: string | null = null;
  success: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private service: SupplierQuoteRequestsService,
    private partsService: PartsService
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    this.returnTo = sanitizeInternalReturnTo(q['returnTo']);
    const preselectedPartId = q['partId'] ?? null;
    const preselectedSupplierId = q['supplierId'] ?? null;
    const requestedQuantity = Number(q['requestedQuantity']);
    this.partsService.list(false).subscribe({
      next: (list) => {
        this.parts = list.filter(p => !p.isLabour);
        this.partRows = this.parts.map(part => ({
          part,
          selected: part.id === preselectedPartId || this.partLinkedToSupplier(part, preselectedSupplierId),
          requestedQuantity: part.id === preselectedPartId && Number.isFinite(requestedQuantity) && requestedQuantity > 0
            ? requestedQuantity
            : this.defaultRequestedQuantity(part)
        }));
      }
    });
  }

  get addSupplierReturnTo(): string {
    const params = new URLSearchParams();
    if (this.returnTo) params.set('returnTo', this.returnTo);
    const firstSelected = this.selectedPartRows[0];
    if (firstSelected) {
      params.set('partId', firstSelected.part.id);
      params.set('requestedQuantity', String(firstSelected.requestedQuantity));
    }
    const query = params.toString();
    return query ? `/supplier-quote-requests?${query}` : '/supplier-quote-requests';
  }

  get selectedPartRows(): PartRequestRow[] {
    return this.partRows.filter(r => r.selected);
  }

  get selectablePartRows(): PartRequestRow[] {
    return this.partRows.filter(r => this.partHasLinkedSupplier(r.part));
  }

  get allSelectablePartsSelected(): boolean {
    const rows = this.selectablePartRows;
    return rows.length > 0 && rows.every(r => r.selected);
  }

  get someSelectablePartsSelected(): boolean {
    return this.selectablePartRows.some(r => r.selected);
  }

  toggleSelectAllParts(checked: boolean): void {
    for (const row of this.partRows) {
      if (!this.partHasLinkedSupplier(row.part)) continue;
      row.selected = checked;
      if (checked && (!row.requestedQuantity || row.requestedQuantity < 1)) {
        row.requestedQuantity = this.defaultRequestedQuantity(row.part);
      }
    }
  }

  get anyDraftSending(): boolean {
    return this.drafts.some(d => d.sending);
  }

  private defaultRequestedQuantity(part: PartDto): number {
    return Math.max(1, Math.round(Number(part.reorderLevel) || 1));
  }

  supplierNames(part: PartDto): string {
    const names = part.supplierNames ?? [];
    if (names.length) return names.join(', ');
    return part.supplierName || 'No supplier linked';
  }

  partHasLinkedSupplier(part: PartDto): boolean {
    return !!part.supplierId || (part.supplierIds?.length ?? 0) > 0;
  }

  partLinkedToSupplier(part: PartDto, supplierId: string | null): boolean {
    if (!supplierId) return false;
    return part.supplierId === supplierId || (part.supplierIds ?? []).includes(supplierId);
  }

  onPartChecked(row: PartRequestRow, checked: boolean): void {
    row.selected = checked;
    if (checked && (!row.requestedQuantity || row.requestedQuantity < 1)) {
      row.requestedQuantity = this.defaultRequestedQuantity(row.part);
    }
  }

  cancel(): void {
    if (this.returnTo) {
      this.router.navigateByUrl(this.returnTo);
      return;
    }
    this.router.navigate(['/supplier-quote-requests/list']);
  }

  save(): void {
    this.error = null;
    this.success = null;
    const validItems = this.selectedPartRows
      .filter(row => this.partHasLinkedSupplier(row.part))
      .map(li => ({
        partId: li.part.id,
        requestedQuantity: li.requestedQuantity && li.requestedQuantity > 0 ? li.requestedQuantity : this.defaultRequestedQuantity(li.part)
      }));
    if (validItems.length === 0) {
      this.error = 'Select at least one part with linked suppliers.';
      return;
    }
    this.submitting = true;
    this.service.buildPartEmailDrafts({ items: validItems }).subscribe({
      next: (response) => {
        this.submitting = false;
        this.drafts = (response.drafts ?? []).map((draft, index) => ({
          ...draft,
          open: index === 0,
          sending: false,
          error: null
        }));
        if (this.drafts.length === 0) {
          this.error = 'No supplier-linked parts were found for the selected items.';
          return;
        }
        this.showDraftModal = true;
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to build supplier drafts.';
        this.submitting = false;
      }
    });
  }

  cancelDraft(): void {
    if (this.sendingAllDrafts || this.anyDraftSending) return;
    this.showDraftModal = false;
  }

  toggleDraft(index: number): void {
    const draft = this.drafts[index];
    if (draft) draft.open = !draft.open;
  }

  sendDraft(index: number): void {
    const draft = this.drafts[index];
    if (!draft || draft.sending || draft.items.length === 0) return;
    if (!draft.toEmail.trim() || !draft.subject.trim() || !draft.body.trim()) {
      this.error = 'Supplier email, subject and body are required.';
      return;
    }
    this.error = null;
    draft.error = null;
    draft.sending = true;
    this.service.sendDraft({
      supplierId: draft.supplierId,
      toEmail: draft.toEmail.trim(),
      subject: draft.subject.trim(),
      body: draft.body,
      items: draft.items.map(i => ({
        partId: i.partId,
        requestedQuantity: i.requestedQuantity
      }))
    }).subscribe({
      next: () => {
        draft.sending = false;
        this.drafts.splice(index, 1);
        this.success = 'Supplier request sent. You can send another draft or create a new request.';
        if (this.drafts.length === 0) {
          this.showDraftModal = false;
          this.resetForm();
        }
      },
      error: (err) => {
        draft.error = err.error?.message || 'Failed to send supplier draft.';
        draft.sending = false;
      }
    });
  }

  sendAllDrafts(): void {
    if (this.drafts.length === 0 || this.sendingAllDrafts) return;
    this.sendingAllDrafts = true;
    const sendNext = () => {
      const draft = this.drafts[0];
      if (!draft) {
        this.sendingAllDrafts = false;
        this.showDraftModal = false;
        this.resetForm();
        this.success = 'All supplier requests sent.';
        return;
      }
      if (!draft.toEmail.trim() || !draft.subject.trim() || !draft.body.trim()) {
        this.sendingAllDrafts = false;
        this.error = `Supplier "${draft.supplierName}" has missing email, subject or body.`;
        return;
      }
      draft.sending = true;
      this.service.sendDraft({
        supplierId: draft.supplierId,
        toEmail: draft.toEmail.trim(),
        subject: draft.subject.trim(),
        body: draft.body,
        items: draft.items.map(i => ({ partId: i.partId, requestedQuantity: i.requestedQuantity }))
      }).subscribe({
        next: () => {
          this.drafts = this.drafts.filter(x => x.supplierId !== draft.supplierId);
          sendNext();
        },
        error: (err) => {
          draft.error = err.error?.message || 'Failed to send supplier draft.';
          draft.sending = false;
          this.sendingAllDrafts = false;
        }
      });
    };
    sendNext();
  }

  private resetForm(): void {
    this.partRows = this.partRows.map(row => ({
      ...row,
      selected: false,
      requestedQuantity: this.defaultRequestedQuantity(row.part)
    }));
    this.drafts = [];
  }
}
