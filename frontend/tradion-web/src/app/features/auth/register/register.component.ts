import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  email = '';
  password = '';
  fullName = '';
  companyName = '';
  companyAddress = '';
  companyPhone = '';
  error = '';
  loading = false;
  showPassword = false;

  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  submit(): void {
    this.error = '';
    this.loading = true;
    this.auth.register({
      email: this.email,
      password: this.password,
      fullName: this.fullName || undefined,
      companyName: this.companyName.trim(),
      companyAddress: this.companyAddress.trim() || undefined,
      companyPhone: this.companyPhone.trim() || undefined
    }).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (err) => {
        const body = err.error;
        if (body?.message) {
          this.error = body.message;
        } else if (body?.errors && typeof body.errors === 'object') {
          const msgs = Object.values(body.errors).flat().filter(Boolean);
          this.error = msgs.length ? msgs.join(' ') : 'Registration failed.';
        } else {
          this.error = 'Registration failed.';
        }
        this.loading = false;
      },
      complete: () => (this.loading = false)
    });
  }
}
