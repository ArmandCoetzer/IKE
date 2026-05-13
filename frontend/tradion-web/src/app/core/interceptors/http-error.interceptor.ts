import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Reads `message` from API JSON and `title` from ProblemDetails-style payloads. */
function messageFromHttpErrorPayload(error: unknown): string | null {
  if (error == null) return null;
  if (typeof error === 'string') {
    const t = error.trim();
    if (!t) return null;
    if (t.startsWith('{')) {
      try {
        const o = JSON.parse(t) as { message?: unknown; title?: unknown };
        if (typeof o.message === 'string' && o.message.trim()) return o.message.trim();
        if (typeof o.title === 'string' && o.title.trim()) return o.title.trim();
      } catch {
        return t;
      }
    }
    return t;
  }
  if (typeof error === 'object') {
    const o = error as { message?: unknown; title?: unknown };
    if (typeof o.message === 'string' && o.message.trim()) return o.message.trim();
    if (typeof o.title === 'string' && o.title.trim()) return o.title.trim();
  }
  return null;
}

/** Map API errors to user-friendly messages. Emit via a small toast service or console for now. */
export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      let message = 'An error occurred.';
      const fromPayload = messageFromHttpErrorPayload(err.error);
      if (fromPayload) {
        message = fromPayload;
      } else if (err.status === 401) {
        message = 'Session expired. Please log in again.';
        const onAuthPage = router.url === '/login' || router.url === '/register' || router.url.startsWith('/accept-invite');
        if (auth.getToken() && !onAuthPage) {
          auth.setReturnUrl(router.url);
          auth.logout();
        }
      } else if (err.status === 403) {
        message = 'You do not have permission for this action.';
      } else if (err.status === 404) {
        message = 'Resource not found.';
      } else if (err.status >= 500) {
        message = 'Server error. Please try again later.';
      }
      console.warn('[HTTP Error]', err.status, message);
      return throwError(() => ({ ...err, userMessage: message }));
    })
  );
};
