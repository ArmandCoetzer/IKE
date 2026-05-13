import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { UsersService, UserListDto, UpdateUserRequest } from '../../../core/services/users.service';

import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-user-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './user-detail.component.html',
  styleUrl: './user-detail.component.scss'
})
export class UserDetailComponent implements OnInit {
  item: UserListDto | null = null;
  loading = true;
  error: string | null = null;
  inviteInfo: string | null = null;
  editMode = false;
  editFirstName = '';
  editLastName = '';
  editPhone = '';
  editOccupation = '';
  editRole = '';
  editIsActive = true;
  editPassword = '';
  roles: string[] = [];
  submitting = false;
  reinviting = false;

  constructor(
    private route: ActivatedRoute,
    private usersService: UsersService
  ) {}

  ngOnInit(): void {
    this.usersService.getRoles().subscribe({ next: (list) => (this.roles = list.filter((r) => r !== 'Admin')) });
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.usersService.get(id).subscribe({
      next: (u) => {
        this.item = u;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load user.';
        this.loading = false;
      }
    });
  }

  startEdit(): void {
    if (!this.item) return;
    this.editMode = true;
    this.editFirstName = this.item.firstName ?? '';
    this.editLastName = this.item.lastName ?? '';
    this.editPhone = this.item.phone ?? '';
    this.editOccupation = this.item.occupation ?? '';
    this.editRole = this.item.role ?? '';
    this.editIsActive = this.item.isActive;
    this.editPassword = '';
    this.error = null;
  }

  cancelEdit(): void {
    this.editMode = false;
    this.error = null;
  }

  reInvite(): void {
    if (!this.item) return;
    this.error = null;
    this.inviteInfo = null;
    this.reinviting = true;
    this.usersService.reInvite(this.item.id).subscribe({
      next: (res) => {
        this.inviteInfo = res.message || 'Invite sent.';
        this.reinviting = false;
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to send invite.';
        this.reinviting = false;
      }
    });
  }

  saveEdit(): void {
    if (!this.item) return;
    if (!this.editFirstName.trim() || !this.editLastName.trim()) {
      this.error = 'First and last name are required.';
      return;
    }
    const req: UpdateUserRequest = {
      firstName: this.editFirstName.trim(),
      lastName: this.editLastName.trim(),
      phone: this.editPhone.trim() || undefined,
      occupation: this.editOccupation.trim() || undefined,
      role: this.editRole || undefined,
      isActive: this.editIsActive
    };
    if (this.editPassword.trim()) req.password = this.editPassword.trim();
    this.submitting = true;
    this.usersService.update(this.item.id, req).subscribe({
      next: (updated) => {
        this.item = updated;
        this.editMode = false;
        this.submitting = false;
        this.error = null;
      },
      error: (err) => {
        this.error = err.error?.message || 'Failed to update user.';
        this.submitting = false;
      }
    });
  }
}
