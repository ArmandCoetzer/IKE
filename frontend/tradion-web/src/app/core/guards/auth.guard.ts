import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn() && !auth.isTokenExpired()) return true;
  if (auth.getToken()) {
    auth.logout();
    return false;
  }
  auth.setReturnUrl(router.url || '/dashboard');
  router.navigate(['/login']);
  return false;
};
