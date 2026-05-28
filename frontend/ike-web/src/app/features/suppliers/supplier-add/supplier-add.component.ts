import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SuppliersService } from '../../../core/services/suppliers.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';

@Component({
  selector: 'app-supplier-add',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
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
  returnTo: string | null = null;

  constructor(
    private suppliersService: SuppliersService,
    private router: Router,
    private route: ActivatedRoute
  ) {
    this.returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
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
      contactPerson: this.contactPerson.trim() || undefined
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
