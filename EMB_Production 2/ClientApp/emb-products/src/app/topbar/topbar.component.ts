import { Component, OnDestroy } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { AuthService } from '../shared/services/auth.service';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './topbar.component.html',
  styleUrls: ['./topbar.component.css']
})
export class TopbarComponent implements OnDestroy {

  pageTitle = 'EMB Production';
  userName = '';
  userImage = '';

  private routerSub: Subscription;

  constructor(
    private router: Router,
    private auth: AuthService
  ) {
    // User info
    this.userName = this.auth.getUserName();
    this.userImage = this.auth.getUserImage();

    // Initial title
    this.setTitle(this.router.url);

    // Update title on navigation
    this.routerSub = this.router.events.subscribe(event => {
      if (event instanceof NavigationEnd) {
        this.setTitle(event.urlAfterRedirects);
      }
    });
  }

  // ✅ THIS METHOD WAS MISSING — NOW FIXED
  private setTitle(url: string): void {

    if (url.includes('/dashboard')) {
      this.pageTitle = 'EMB Production';
    }
    else if (url.includes('/bom')) {
      this.pageTitle = 'Bill of Materials';
    }
    else if (url.includes('/products')) {
      this.pageTitle = 'Products';
    }
    else if (url.includes('/threads')) {
      this.pageTitle = 'Threads';
    }
    else if (url.includes('/work-orders')) {
      this.pageTitle = 'Work Orders';
    }
    else if (url.includes('/job-orders')) {
      this.pageTitle = 'Job Orders';
    }
    else if (url.includes('/dispatch')) {
      this.pageTitle = 'Dispatch';
    }
    else {
      this.pageTitle = 'EMB Production';
    }
  }

  goProfile(): void {
    this.router.navigate(['/settings/profile']);
  }

  goSettings(): void {
    this.router.navigate(['/settings/general']);
  }

  logout(): void {
    this.auth.logout().subscribe(() => {
      this.router.navigate(['/login']);
    });
  }

  ngOnDestroy(): void {
    this.routerSub?.unsubscribe();
  }
}
