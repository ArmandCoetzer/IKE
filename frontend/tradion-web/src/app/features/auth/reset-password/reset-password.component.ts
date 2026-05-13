import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './reset-password.component.html'
})
export class ResetPasswordComponent implements OnInit {
  email = '';
  token = '';
  newPassword = '';
  confirmPassword = '';
  loading = false;
  error = '';
  message = '';
  showNewPassword = false;
  showConfirmPassword = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    this.email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
  }

  get passwordMismatch(): boolean {
    return !!this.newPassword && !!this.confirmPassword && this.newPassword !== this.confirmPassword;
  }

  submit(): void {
    if (!this.email || !this.token) {
      this.error = 'Invalid reset link.';
      return;
    }
    if (this.passwordMismatch) {
      this.error = 'Passwords do not match.';
      return;
    }
    this.loading = true;
    this.error = '';
    this.message = '';
    this.auth.resetPassword({ email: this.email, token: this.token, newPassword: this.newPassword }).subscribe({
      next: () => {
        this.message = 'Password updated. Redirecting to sign in...';
        setTimeout(() => this.router.navigate(['/login']), 1200);
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Could not reset password.';
      },
      complete: () => {
        this.loading = false;
      }
    });
  }
}
