import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PartsService, PartDto, UpdatePartRequest } from '../../../core/services/parts.service';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-part-edit',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './part-edit.component.html',
  styleUrl: './part-edit.component.scss'
})
export class PartEditComponent implements OnInit {
  readonly labourUnit = 'Hours';
  readonly labourUnitDisplay = 'Hours (per hour)';
  readonly unitOptions = ['unit/s', 'Box', 'Pack', 'Kg', 'g', 'L', 'ml', 'm', 'cm', 'mm', 'Set', 'Pair', 'Roll', 'Hours', 'Days'];
  id: string | null = null;
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
  newSupplierName = '';
  addingSupplier = false;
  loading = true;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private partsService: PartsService,
    private suppliersService: SuppliersService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.loading = false;
      return;
    }
    this.suppliersService.list().subscribe({ next: (s) => (this.suppliers = s) });
    this.partsService.get(this.id).subscribe({
      next: (p: PartDto) => {
        this.name = p.name ?? '';
        this.description = p.description ?? '';
        this.partNumber = p.partNumber ?? '';
        this.quantity = p.quantity ?? 0;
        this.reorderLevel = p.reorderLevel ?? 0;
        this.unit = p.unit ?? '';
        this.unitPrice = p.unitPrice ?? 0;
        this.isLabour = p.isLabour ?? false;
        if (this.isLabour) this.unit = this.labourUnit;
        this.supplierId = p.supplierId ?? null;
        this.supplierIds = p.supplierIds ?? (p.supplierId ? [p.supplierId] : []);
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load part.';
        this.loading = false;
      }
    });
  }

  onIsLabourChange(): void {
    if (this.isLabour) {
      this.unit = this.labourUnit;
      this.quantity = 0;
      this.reorderLevel = 0;
      this.supplierId = null;
      this.supplierIds = [];
    }
  }

  save(): void {
    if (!this.isLabour && !this.supplierId) {
      this.error = 'Supplier is required for non-labour parts.';
      return;
    }
    if (!this.id) return;
    this.error = null;
    if (!this.name.trim()) {
      this.error = 'Name is required.';
      return;
    }
    if (!this.isLabour && (this.quantity < 0 || this.reorderLevel < 0)) {
      this.error = 'Quantity and reorder level cannot be negative.';
      return;
    }
    const body: UpdatePartRequest = {
      name: this.name.trim(),
      description: this.description.trim() || undefined,
      partNumber: this.partNumber.trim() || undefined,
      quantity: this.isLabour ? 0 : this.quantity,
      reorderLevel: this.isLabour ? 0 : this.reorderLevel,
      unit: this.isLabour ? this.labourUnit : (this.unit.trim() || undefined),
      unitPrice: this.unitPrice,
      isLabour: this.isLabour,
      supplierId: this.isLabour ? null : this.supplierId,
      supplierIds: this.isLabour ? [] : this.supplierIds
    };
    this.submitting = true;
    this.partsService.update(this.id, body).subscribe({
      next: (part) => {
        this.submitting = false;
        this.router.navigate(['/parts', part.id]);
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to update part.';
      }
    });
  }

  addSupplierInline(): void {
    this.error = null;
    const name = this.newSupplierName.trim();
    if (!name || this.addingSupplier) return;
    this.addingSupplier = true;
    this.suppliersService.create({ name }).subscribe({
      next: (created) => {
        this.suppliers = [...this.suppliers, created].sort((a, b) => a.name.localeCompare(b.name));
        this.supplierId = created.id;
        if (!this.supplierIds.includes(created.id)) this.supplierIds = [...this.supplierIds, created.id];
        this.newSupplierName = '';
        this.addingSupplier = false;
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to create supplier.';
        this.addingSupplier = false;
      }
    });
  }

  onSupplierSelectionChange(): void {
    if (this.supplierIds.length > 0) this.supplierId = this.supplierIds[0];
    else this.supplierId = null;
  }
}
