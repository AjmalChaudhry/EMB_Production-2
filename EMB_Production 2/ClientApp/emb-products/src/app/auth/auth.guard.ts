import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../shared/services/auth.service';

export const AuthGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  console.log('AuthGuard → Checking access for:', state.url);

  if (auth.isLoggedInSync()) {
    console.log('AuthGuard → User authenticated, access allowed');
    return true;
  }

  console.log('AuthGuard → Not authenticated, redirecting to login...');
  router.navigate(['/login'], {
    queryParams: { returnUrl: state.url }
  });

  return false;
};
