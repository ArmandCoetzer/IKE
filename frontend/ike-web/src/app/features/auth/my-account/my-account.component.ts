import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-my-account',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './my-account.component.html'
})
export class MyAccountComponent implements OnInit {
  loading = true;
  savingProfile = false;
  savingPassword = false;
  profileMessage = '';
  profileError = '';
  passwordMessage = '';
  passwordError = '';

  firstName = '';
  lastName = '';
  phone = '';
  email = '';
  showCurrentPassword = false;
  showNewPassword = false;
  showConfirmPassword = false;

  currentPassword = '';
  newPassword = '';
  confirmPassword = '';

  constructor(private auth: AuthService) {}

  ngOnInit(): void {
    this.auth.getProfile().subscribe({
      next: (p) => {
        this.email = p.email ?? '';
        this.firstName = p.firstName ?? '';
        this.lastName = p.lastName ?? '';
        this.phone = p.phone ?? '';
      },
      error: () => {
        this.profileError = 'Could not load your account details.';
      },
      complete: () => {
        this.loading = false;
      }
    });
  }

  saveProfile(): void {
    this.profileError = '';
    this.profileMessage = '';
    this.savingProfile = true;
    this.auth.updateProfile({
      email: this.email.trim() || undefined,
      firstName: this.firstName.trim() || undefined,
      lastName: this.lastName.trim() || undefined,
      phone: this.phone.trim() || undefined
    }).subscribe({
      next: () => {
        this.profileMessage = 'Profile updated.';
      },
      error: (err) => {
        this.profileError = err.error?.message ?? 'Could not update profile.';
      },
      complete: () => {
        this.savingProfile = false;
      }
    });
  }

  savePassword(): void {
    this.passwordError = '';
    this.passwordMessage = '';
    if (!this.currentPassword || !this.newPassword) {
      this.passwordError = 'Current and new password are required.';
      return;
    }
    if (this.newPassword.length < 8) {
      this.passwordError = 'New password must be at least 8 characters.';
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.passwordError = 'New password and confirmation do not match.';
      return;
    }

    this.savingPassword = true;
    this.auth.changePassword({ currentPassword: this.currentPassword, newPassword: this.newPassword }).subscribe({
      next: () => {
        this.passwordMessage = 'Password updated.';
        this.currentPassword = '';
        this.newPassword = '';
        this.confirmPassword = '';
      },
      error: (err) => {
        this.passwordError = err.error?.message ?? 'Could not update password.';
      },
      complete: () => {
        this.savingPassword = false;
      }
    });
  }
}
