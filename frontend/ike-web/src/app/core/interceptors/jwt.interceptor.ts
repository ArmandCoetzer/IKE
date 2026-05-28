import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

function isAuthEndpoint(url: string): boolean {
  const path = (() => {
    try {
      const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost';
      return new URL(url, origin).pathname.toLowerCase();
    } catch {
      return url.toLowerCase();
    }
  })();
  return path.endsWith('/api/auth/login') ||
    path.endsWith('/api/auth/login-web') ||
    path.endsWith('/api/auth/login-mobile') ||
    path.includes('/authentication') ||
    path.includes('/authentication/2fa/');
}

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  if (isAuthEndpoint(req.url)) {
    return next(req);
  }
  const auth = inject(AuthService);
  const token = auth.getToken();
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  return next(req);
};
