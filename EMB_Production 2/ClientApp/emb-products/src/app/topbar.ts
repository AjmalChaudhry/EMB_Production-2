import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from './shared/services/auth.service';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="topbar px-3 d-flex justify-content-between align-items-center">
      <h5 class="m-0">Dashboard</h5>

      <div class="d-flex align-items-center">
        <span class="me-3">{{ auth.getUserEmail() }}</span>
        <img [src]="auth.getUserImage()" width="38" class="rounded-circle">
      </div>
    </div>
  `,
  styles: [
    `
      .topbar {
        height: 60px;
        background: #ffffff;
        border-bottom: 1px solid #ddd;
      }
    `
  ]
})
export class TopbarComponent {
  constructor(public auth: AuthService) { }
}
