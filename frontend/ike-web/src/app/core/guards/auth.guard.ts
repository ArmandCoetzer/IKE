import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn() && !auth.isTokenExpired()) return true;
  if (auth.getToken()) {
    auth.clearSession();
    auth.setReturnUrl(state.url || '/dashboard');
    return router.createUrlTree(['/login']);
  }
  auth.setReturnUrl(state.url || router.url || '/dashboard');
  return router.createUrlTree(['/login']);
};
