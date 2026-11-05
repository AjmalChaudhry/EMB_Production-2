import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { DashboardApiService, ChangePasswordResponse } from '../shared/services/dashboard-api.service';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './change-password.component.html',
  styleUrls: ['./change-password.component.css']
})

export class ChangePasswordComponent {
  oldPassword: string = '';
  newPassword: string = '';
  confirmPassword: string = '';
  message: string = '';
  messageType: 'success' | 'error' | '' = '';
  submitting: boolean = false;

  constructor(
    private dashboardApiService: DashboardApiService,
    private router: Router
  ) { }

  // ... rest of your methods remain the same
  submit(): void {
    if (this.newPassword !== this.confirmPassword) {
      this.message = 'New password and confirmation do not match';
      this.messageType = 'error';
      return;
    }

    if (this.newPassword.length < 6) {
      this.message = 'New password must be at least 6 characters long';
      this.messageType = 'error';
      return;
    }

    this.submitting = true;
    this.message = '';
    this.messageType = '';

    this.dashboardApiService.changePassword(this.oldPassword, this.newPassword)
      .subscribe({
        next: (response: ChangePasswordResponse) => {
          this.submitting = false;
          if (response.success) {
            this.message = 'Password updated successfully!';
            this.messageType = 'success';
            this.oldPassword = '';
            this.newPassword = '';
            this.confirmPassword = '';

            setTimeout(() => {
              this.router.navigate(['/dashboard']);
            }, 2000);
          } else {
            this.message = response.message || 'Failed to update password';
            this.messageType = 'error';
          }
        },
        error: (error) => {
          this.submitting = false;
          this.message = error.message || 'An error occurred while updating password';
          this.messageType = 'error';
          console.error('Password change error:', error);
        }
      });
  }

  cancel(): void {
    this.router.navigate(['/dashboard']);
  }

  passwordsMatch(): boolean {
    return this.newPassword === this.confirmPassword;
  }
}
