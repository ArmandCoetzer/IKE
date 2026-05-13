import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SuppliersService, SupplierDto } from '../../../core/services/suppliers.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

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
  error: string | null = null;
  name = '';
  email = '';
  websiteUrl = '';
  phone = '';
  contactPerson = '';

  constructor(
    private route: ActivatedRoute,
    private suppliersService: SuppliersService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id');
    if (!this.id) {
      this.error = 'Supplier not found.';
      this.loading = false;
      return;
    }

    this.suppliersService.list().subscribe({
      next: (list) => {
        const supplier = list.find(s => s.id === this.id);
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
      contactPerson: this.contactPerson.trim() || undefined
    }).subscribe({
      next: () => {
        this.saving = false;
        this.router.navigate(['/suppliers']);
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to update supplier.';
        this.saving = false;
      }
    });
  }
}
