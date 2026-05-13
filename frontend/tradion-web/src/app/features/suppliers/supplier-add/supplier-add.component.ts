import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { SuppliersService } from '../../../core/services/suppliers.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-supplier-add',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PageHeaderComponent],
  templateUrl: './supplier-add.component.html'
})
export class SupplierAddComponent {
  saving = false;
  error: string | null = null;
  name = '';
  email = '';
  websiteUrl = '';
  phone = '';
  contactPerson = '';

  constructor(
    private suppliersService: SuppliersService,
    private router: Router
  ) {}

  save(): void {
    this.error = null;
    if (!this.name.trim() || !this.email.trim() || this.saving) return;
    this.saving = true;
    this.suppliersService.create({
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
        this.error = err.error?.message || 'Failed to create supplier.';
        this.saving = false;
      }
    });
  }
}
