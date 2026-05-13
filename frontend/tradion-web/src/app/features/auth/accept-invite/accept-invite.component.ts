import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { InviteService, InviteInfo } from '../../../core/services/invite.service';

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './accept-invite.component.html',
  styleUrl: './accept-invite.component.scss'
})
export class AcceptInviteComponent implements OnInit {
  token = '';
  inviteInfo: InviteInfo | null = null;
  loading = true;
  submitting = false;
  error: string | null = null;
  successMessage: string | null = null;

  firstName = '';
  lastName = '';
  phone = '';
  password = '';
  confirmPassword = '';
  showPassword = false;
  showConfirmPassword = false;

  constructor(
    private inviteService: InviteService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.token = params['token'] ?? '';
      if (!this.token) {
        this.error = 'Invalid or missing invite link.';
        this.loading = false;
        return;
      }
      this.loadInviteInfo();
    });
  }

  private loadInviteInfo(): void {
    this.loading = true;
    this.error = null;
    this.inviteService.getInviteInfo(this.token).subscribe({
      next: (info) => {
        this.inviteInfo = info;
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.error = err.error?.message ?? 'Invalid or expired link.';
      }
    });
  }

  get isClient(): boolean {
    return this.inviteInfo?.type === 'client';
  }

  get isEmployee(): boolean {
    return this.inviteInfo?.type === 'employee';
  }

  submit(): void {
    this.error = null;
    if (!this.password || this.password.length < 8) {
      this.error = 'Password must be at least 8 characters.';
      return;
    }
    if (this.password !== this.confirmPassword) {
      this.error = 'Password and confirm password must match.';
      return;
    }
    if (this.isClient) {
      if (!this.firstName.trim() || !this.lastName.trim()) {
        this.error = 'First name and last name are required.';
        return;
      }
    }
    this.submitting = true;
    const body = {
      token: this.token,
      password: this.password,
      confirmPassword: this.confirmPassword,
      ...(this.isClient && {
        firstName: this.firstName.trim(),
        lastName: this.lastName.trim(),
        phone: this.phone.trim() || undefined
      })
    };
    this.inviteService.completeInvite(body).subscribe({
      next: (res) => {
        this.successMessage = res.message ?? 'Registration complete. You can now log in.';
        this.submitting = false;
        setTimeout(() => this.router.navigate(['/login']), 2000);
      },
      error: (err) => {
        this.error = err.error?.message ?? 'Failed to complete registration.';
        this.submitting = false;
      }
    });
  }
}
