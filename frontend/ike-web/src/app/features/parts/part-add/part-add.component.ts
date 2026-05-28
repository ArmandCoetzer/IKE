import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PartsService, CreatePartRequest } from '../../../core/services/parts.service';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-part-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './part-add.component.html',
  styleUrl: './part-add.component.scss'
})
export class PartAddComponent implements OnInit {
  readonly labourUnit = 'Hours';
  readonly labourUnitDisplay = 'Hours (per hour)';
  readonly unitOptions = ['unit/s', 'Box', 'Pack', 'Kg', 'g', 'L', 'ml', 'm', 'cm', 'mm', 'Set', 'Pair', 'Roll', 'Hours', 'Days'];
  name = '';
  description = '';
  partNumber = '';
  quantity = 0;
  reorderLevel = 0;
  unit = '';
  unitPrice = 0;
  isLabour = false;
  supplierId: string | null = null;
  supplierIds: string[] = [];
  suppliers: SupplierDto[] = [];
  submitting = false;
  error: string | null = null;
  returnTo: string | null = null;
  forCompanyId: string | null = null;

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private partsService: PartsService,
    private suppliersService: SuppliersService
  ) {}

  goBack(): void {
    const back = sanitizeInternalReturnTo(this.returnTo);
    if (back) this.router.navigateByUrl(back);
    else this.router.navigate(['/parts']);
  }

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.returnTo = sanitizeInternalReturnTo(q.get('returnTo'));
    this.forCompanyId = q.get('forCompanyId');
    this.suppliersService.list().subscribe({ next: (s) => (this.suppliers = s) });
  }

  onIsLabourChange(): void {
    if (this.isLabour) {
      this.unit = this.labourUnit;
      this.supplierId = null;
      this.supplierIds = [];
      this.quantity = 0;
      this.reorderLevel = 0;
    }
  }

  get addSupplierReturnTo(): string {
    const params = new URLSearchParams();
    if (this.returnTo) params.set('returnTo', this.returnTo);
    if (this.forCompanyId) params.set('forCompanyId', this.forCompanyId);
    const query = params.toString();
    return query ? `/parts/new?${query}` : '/parts/new';
  }

  isSupplierSelected(supplierId: string): boolean {
    return this.supplierIds.includes(supplierId);
  }

  onSupplierChecked(supplierId: string, checked: boolean): void {
    if (checked) {
      if (!this.supplierIds.includes(supplierId)) this.supplierIds = [...this.supplierIds, supplierId];
    } else {
      this.supplierIds = this.supplierIds.filter(id => id !== supplierId);
    }
    this.supplierId = this.supplierIds[0] ?? null;
  }

  save(): void {
    if (!this.isLabour && this.supplierIds.length === 0) {
      this.error = 'Select at least one supplier for non-labour parts.';
      return;
    }
    this.error = null;
    if (!this.name.trim()) {
      this.error = 'Name is required.';
      return;
    }
    if (this.quantity < 0) {
      this.error = 'Quantity cannot be negative.';
      return;
    }
    if (this.reorderLevel < 0) {
      this.error = 'Reorder level cannot be negative.';
      return;
    }
    const body: CreatePartRequest = {
      name: this.name.trim(),
      description: this.description.trim() || undefined,
      partNumber: this.partNumber.trim() || undefined,
      quantity: this.isLabour ? 0 : this.quantity,
      reorderLevel: this.isLabour ? 0 : this.reorderLevel,
      unit: this.unit.trim() || (this.isLabour ? this.labourUnit : undefined),
      unitPrice: this.unitPrice,
      isLabour: this.isLabour,
      supplierId: this.isLabour ? undefined : (this.supplierIds[0] ?? undefined),
      supplierIds: this.isLabour ? undefined : this.supplierIds
    };
    this.submitting = true;
    this.partsService.create(body, this.forCompanyId ?? undefined).subscribe({
      next: (part) => {
        this.submitting = false;
        const back = sanitizeInternalReturnTo(this.returnTo);
        if (back) this.router.navigateByUrl(back);
        else this.router.navigate(['/parts', part.id]);
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to create part.';
      }
    });
  }
}
