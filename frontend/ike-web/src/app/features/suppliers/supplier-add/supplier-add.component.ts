import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SuppliersService } from '../../../core/services/suppliers.service';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';

@Component({
  selector: 'app-supplier-add',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './supplier-add.component.html'
})
export class SupplierAddComponent implements OnInit {
  saving = false;
  error: string | null = null;
  name = '';
  email = '';
  websiteUrl = '';
  phone = '';
  contactPerson = '';
  returnTo: string | null = null;
  parts: PartDto[] = [];
  partSearch = '';
  selectedPartIds: string[] = [];

  constructor(
    private suppliersService: SuppliersService,
    private partsService: PartsService,
    private router: Router,
    private route: ActivatedRoute
  ) {
    this.returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  ngOnInit(): void {
    this.partsService.list().subscribe({
      next: (parts) => (this.parts = parts.filter(p => !p.isLabour))
    });
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

  goBack(): void {
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/suppliers']);
  }

  save(): void {
    this.error = null;
    if (!this.name.trim() || !this.email.trim() || this.saving) return;
    this.saving = true;
    this.suppliersService.create({
      name: this.name.trim(),
      email: this.email.trim(),
      websiteUrl: this.websiteUrl.trim() || undefined,
      phone: this.phone.trim() || undefined,
      contactPerson: this.contactPerson.trim() || undefined,
      partIds: this.selectedPartIds
    }).subscribe({
      next: () => {
        this.saving = false;
        this.goBack();
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to create supplier.';
        this.saving = false;
      }
    });
  }
}
