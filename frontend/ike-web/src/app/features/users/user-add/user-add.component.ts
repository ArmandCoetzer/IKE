import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { UsersService } from '../../../core/services/users.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-user-add',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './user-add.component.html',
  styleUrl: './user-add.component.scss'
})
export class UserAddComponent implements OnInit {
  email = '';
  firstName = '';
  lastName = '';
  phone = '';
  occupation = '';
  role = '';
  roles: string[] = [];
  loading = false;
  submitting = false;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private usersService: UsersService,
    private router: Router
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParams;
    if (q['role'] && q['role'].trim()) this.role = q['role'].trim();
    this.loading = true;
    this.usersService.getRoles(true).subscribe({
      next: (list) => {
        this.roles = list.filter((r) => r !== 'Admin');
        if (this.roles.length && !this.roles.includes(this.role)) this.role = this.roles[0];
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  private isValidEmail(email: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  }

  save(): void {
    this.error = null;
    const email = this.email.trim();
    if (!email) {
      this.error = 'Email is required.';
      return;
    }
    if (!this.isValidEmail(email)) {
      this.error = 'Enter a valid email address, for example name@company.com.';
      return;
    }
    if (!this.firstName.trim()) {
      this.error = 'First name is required.';
      return;
    }
    if (!this.lastName.trim()) {
      this.error = 'Last name is required.';
      return;
    }
    if (!this.role) {
      this.error = 'Role is required.';
      return;
    }
    this.submitting = true;
    this.usersService.create({
      email,
      firstName: this.firstName.trim(),
      lastName: this.lastName.trim(),
      phone: this.phone.trim() || undefined,
      occupation: this.occupation.trim() || undefined,
      role: this.role
    }).subscribe({
      next: (user) => {
        this.submitting = false;
        const returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
        if (returnTo) {
          this.router.navigateByUrl(returnTo);
        } else {
          this.router.navigate(['/users', user.id]);
        }
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.userMessage || err.error?.message || 'Failed to create employee.';
      }
    });
  }
}
