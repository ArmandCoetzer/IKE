import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

function fieldLabel(name: string): string {
  return name
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .replace(/^./, c => c.toUpperCase());
}

function friendlyValidationMessage(field: string, message: string): string {
  const label = fieldLabel(field);
  const trimmed = message.trim();
  if (!trimmed) return `${label} is invalid.`;
  const emailInvalid = /not a valid e-?mail address/i.test(trimmed);
  if (emailInvalid) return `${label} is not a valid email address.`;
  return trimmed;
}

function validationErrorsMessage(errors: unknown): string | null {
  if (!errors || typeof errors !== 'object') return null;
  const messages: string[] = [];
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value)) {
      for (const item of value) {
        if (typeof item === 'string' && item.trim()) {
          messages.push(friendlyValidationMessage(field, item));
        }
      }
    } else if (typeof value === 'string' && value.trim()) {
      messages.push(friendlyValidationMessage(field, value));
    }
  }
  if (messages.length === 0) return null;
  return messages.slice(0, 3).join(' ');
}

/** Reads `message` from API JSON and validation details from ProblemDetails-style payloads. */
function messageFromHttpErrorPayload(error: unknown): string | null {
  if (error == null) return null;
  if (typeof error === 'string') {
    const t = error.trim();
    if (!t) return null;
    if (t.startsWith('{')) {
      try {
        const o = JSON.parse(t) as { message?: unknown; title?: unknown; errors?: unknown };
        if (typeof o.message === 'string' && o.message.trim()) return o.message.trim();
        const validation = validationErrorsMessage(o.errors);
        if (validation) return validation;
        if (typeof o.title === 'string' && o.title.trim()) return o.title.trim();
      } catch {
        return t;
      }
    }
    return t;
  }
  if (typeof error === 'object') {
    const o = error as { message?: unknown; title?: unknown; errors?: unknown };
    if (typeof o.message === 'string' && o.message.trim()) return o.message.trim();
    const validation = validationErrorsMessage(o.errors);
    if (validation) return validation;
    if (typeof o.title === 'string' && o.title.trim()) return o.title.trim();
  }
  return null;
}

function requestPath(url: string): string {
  try {
    const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost';
    return new URL(url, origin).pathname.toLowerCase();
  } catch {
    return url.toLowerCase();
  }
}

function isLoginOrAuthChallengeEndpoint(url: string): boolean {
  const path = requestPath(url);
  return path.endsWith('/api/auth/login') ||
    path.endsWith('/api/auth/login-web') ||
    path.endsWith('/api/auth/login-mobile') ||
    path.includes('/authentication') ||
    path.includes('/authentication/2fa/');
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
        const authEndpoint = isLoginOrAuthChallengeEndpoint(req.url);
        if (auth.getToken() && !onAuthPage && !authEndpoint) {
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
