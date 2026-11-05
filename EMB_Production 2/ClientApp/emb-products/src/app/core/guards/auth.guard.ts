import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { AuthService } from '../../shared/services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {

  constructor(
    private auth: AuthService,
    private router: Router
  ) { }

  canActivate(): boolean | UrlTree {
    const isLoggedIn = this.auth.isLoggedIn();

    if (isLoggedIn) {
      return true;
    }

    return this.router.parseUrl('/emb-products/login');
  }
}
