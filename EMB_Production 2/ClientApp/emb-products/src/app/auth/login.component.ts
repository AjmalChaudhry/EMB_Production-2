import { Component } from '@angular/core';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../shared/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent {
  email = '';
  password = '';
  showPassword = false;

  submitting = false;
  loginError = '';

  readonly currentYear = new Date().getFullYear();

  constructor(
    private auth: AuthService,
    private router: Router,
    private route: ActivatedRoute
  ) { }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }

  login(): void {
    this.loginError = '';

    if (!this.email || !this.password) {
      this.loginError = 'Email and password are required.';
      return;
    }

    this.submitting = true;

    this.auth.login(this.email, this.password).subscribe({
      next: (res) => {
        this.submitting = false;

        if (!res.success) {
          this.loginError = res.message || 'Invalid credentials.';
          return;
        }

        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') || '/dashboard';
        this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.submitting = false;
        console.error('Login error', err);
        this.loginError = err?.error?.message || 'Login failed. Check your email or password.';
      }
    });
  }
}
