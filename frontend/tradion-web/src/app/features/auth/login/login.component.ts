import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule, NgForm } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  email = '';
  password = '';
  rememberMe = true;
  error = '';
  loading = false;
  showPassword = false;

  constructor(
    private auth: AuthService,
    private router: Router
  ) {}

  get passwordFeedback(): string {
    if (!this.password) return '';
    const checks = [
      { ok: this.password.length >= 8, text: '8+ chars' },
      { ok: /[A-Z]/.test(this.password), text: 'uppercase' },
      { ok: /[a-z]/.test(this.password), text: 'lowercase' },
      { ok: /\d/.test(this.password), text: 'number' }
    ];
    const missing = checks.filter(c => !c.ok).map(c => c.text);
    return missing.length ? `Password still needs: ${missing.join(', ')}` : 'Password format looks good.';
  }

  submit(form?: NgForm): void {
    if (form && form.invalid) return;
    this.error = '';
    this.loading = true;
    this.auth.login({ email: this.email, password: this.password, rememberMe: this.rememberMe }).subscribe({
      next: () => {
        const returnUrl = this.auth.consumeReturnUrl();
        this.router.navigateByUrl(returnUrl || '/dashboard');
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Login failed.';
        this.loading = false;
      },
      complete: () => (this.loading = false)
    });
  }
}
