import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { ToastService } from '../../../core/services/toast.service';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';

@Component({
  selector: 'app-supplier-edit',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PageHeaderComponent],
  templateUrl: './supplier-edit.component.html'
})
export class SupplierEditComponent implements OnInit {
  id: string | null = null;
  loading = true;
  saving = false;
  deleting = false;
  error: string | null = null;
  name = '';
  email = '';
  websiteUrl = '';
  phone = '';
  contactPerson = '';
  parts: PartDto[] = [];
  partSearch = '';
  selectedPartIds: string[] = [];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private suppliersService: SuppliersService,
    private partsService: PartsService,
    private toast: ToastService,
    private confirmDialog: ConfirmDialogService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.error = 'Supplier not found.';
      this.loading = false;
      return;
    }

    forkJoin({
      suppliers: this.suppliersService.list(),
      parts: this.partsService.list()
    }).subscribe({
      next: ({ suppliers, parts }) => {
        this.parts = parts.filter(p => !p.isLabour);
        const supplier = suppliers.find(s => s.id === this.id);
        if (!supplier) {
          this.error = 'Supplier not found.';
          this.loading = false;
          return;
        }
        this.applySupplier(supplier);
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load supplier.';
        this.loading = false;
      }
    });
  }

  private applySupplier(s: SupplierDto): void {
    this.name = s.name || '';
    this.email = s.email || '';
    this.websiteUrl = s.websiteUrl || '';
    this.phone = s.phone || '';
    this.contactPerson = s.contactPerson || '';
    this.selectedPartIds = s.partIds ?? [];
  }

  get filteredParts(): PartDto[] {
    const q = this.partSearch.trim().toLowerCase();
    if (!q) return this.parts;
    return this.parts.filter(p =>
      (p.name ?? '').toLowerCase().includes(q)
      || (p.partNumber ?? '').toLowerCase().includes(q)
    );
  }

  get linkedParts(): PartDto[] {
    return this.parts.filter(p => this.isPartSelected(p.id));
  }

  get availableParts(): PartDto[] {
    return this.filteredParts.filter(p => !this.isPartSelected(p.id));
  }

  isPartSelected(partId: string): boolean {
    return this.selectedPartIds.includes(partId);
  }

  onPartChecked(partId: string, checked: boolean): void {
    if (checked) {
      if (!this.selectedPartIds.includes(partId)) this.selectedPartIds = [...this.selectedPartIds, partId];
    } else {
      this.selectedPartIds = this.selectedPartIds.filter(id => id !== partId);
    }
  }

  linkPart(partId: string): void {
    this.onPartChecked(partId, true);
  }

  unlinkPart(partId: string): void {
    this.onPartChecked(partId, false);
  }

  selectFilteredParts(): void {
    const ids = new Set(this.selectedPartIds);
    this.availableParts.forEach(p => ids.add(p.id));
    this.selectedPartIds = Array.from(ids);
  }

  clearSelectedParts(): void {
    this.selectedPartIds = [];
  }

  save(): void {
    if (!this.id || this.saving) return;
    this.error = null;
    if (!this.name.trim() || !this.email.trim()) {
      this.error = 'Supplier name and email are required.';
      return;
    }
    this.saving = true;
    this.suppliersService.update(this.id, {
      name: this.name.trim(),
      email: this.email.trim(),
      websiteUrl: this.websiteUrl.trim() || undefined,
      phone: this.phone.trim() || undefined,
      contactPerson: this.contactPerson.trim() || undefined,
      partIds: this.selectedPartIds
    }).subscribe({
      next: (supplier) => {
        this.saving = false;
        this.applySupplier(supplier);
        this.partsService.list().subscribe({
          next: (parts) => (this.parts = parts.filter(p => !p.isLabour))
        });
        this.toast.success('Supplier updated.');
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to update supplier.';
        this.saving = false;
        this.toast.error(this.error!);
      }
    });
  }

  async deleteSupplier(): Promise<void> {
    if (!this.id || this.deleting) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete supplier',
      message: `Delete supplier "${this.name}"? Safe part links and draft requests will be removed first.`,
      confirmText: 'Delete',
      confirmButtonClass: 'btn-danger'
    });
    if (!confirmed) return;
    this.error = null;
    this.deleting = true;
    this.suppliersService.delete(this.id).subscribe({
      next: () => {
        this.toast.success('Supplier deleted.');
        this.router.navigateByUrl('/suppliers');
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to delete supplier.';
        this.deleting = false;
        this.toast.error(this.error!);
      }
    });
  }
}
