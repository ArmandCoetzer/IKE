import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './forgot-password.component.html'
})
export class ForgotPasswordComponent {
  email = '';
  loading = false;
  message = '';
  error = '';

  constructor(private auth: AuthService) {}

  submit(): void {
    this.loading = true;
    this.error = '';
    this.message = '';
    this.auth.forgotPassword({ email: this.email }).subscribe({
      next: () => {
        this.message = 'If an account exists for that email, a reset link has been sent.';
      },
      error: () => {
        // Keep response generic and non-blocking to avoid account enumeration
        // and to avoid exposing transient email infrastructure issues to users.
        this.message = 'If an account exists for that email, a reset link has been sent.';
      },
      complete: () => {
        this.loading = false;
      }
    });
  }
}
