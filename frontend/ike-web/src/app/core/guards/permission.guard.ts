import { inject } from '@angular/core';
import { Router, CanActivateFn, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { catchError, map, of } from 'rxjs';

/** Requires every permission name in route.data['permissions'] (string or string[]). */
export const permissionGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const rawAll = route.data['permissions'];
  const requiredAll = (Array.isArray(rawAll) ? rawAll : rawAll != null ? [String(rawAll)] : []) as string[];
  const rawAny = route.data['permissionsAny'];
  const requiredAny = (Array.isArray(rawAny) ? rawAny : rawAny != null ? [String(rawAny)] : []) as string[];
  const rawRoles = route.data['roles'];
  const allowedRoles = (Array.isArray(rawRoles) ? rawRoles : rawRoles != null ? [String(rawRoles)] : []) as string[];
  const rawDisallowRoles = route.data['disallowRoles'];
  const disallowedRoles = (Array.isArray(rawDisallowRoles) ? rawDisallowRoles : rawDisallowRoles != null ? [String(rawDisallowRoles)] : []) as string[];
  const dashboard = (): UrlTree => router.createUrlTree(['/dashboard']);
  const login = (): UrlTree => router.createUrlTree(['/login']);

  const evaluateAccess = (): boolean | UrlTree => {
    const role = (auth.role() || '').trim().toLowerCase();

    if (disallowedRoles.length > 0 && disallowedRoles.some(r => role === r.trim().toLowerCase())) {
      return dashboard();
    }
    if (allowedRoles.length > 0 && !allowedRoles.some(r => role === r.trim().toLowerCase())) {
      return dashboard();
    }
    if (requiredAll.length > 0 && !requiredAll.every(p => auth.hasPermission(p))) {
      return dashboard();
    }
    if (requiredAny.length > 0 && !requiredAny.some(p => auth.hasPermission(p))) {
      return dashboard();
    }
    return true;
  };

  // On hard refresh, auth token can exist while role/permissions are not hydrated yet.
  // Wait for /auth/me once before enforcing permission checks to avoid redirecting to dashboard.
  if (auth.getToken() && !auth.isTokenExpired() && !auth.user()) {
    return auth.me().pipe(
      map(() => evaluateAccess()),
      catchError(() => {
        auth.clearSession();
        auth.setReturnUrl(state.url || '/dashboard');
        return of(login());
      })
    );
  }

  return evaluateAccess();
};
