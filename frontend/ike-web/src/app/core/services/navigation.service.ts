import { Injectable } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

/** Allows only same-origin relative paths (blocks open redirects e.g. //evil.com). */
export function sanitizeInternalReturnTo(raw: string | null | undefined): string | null {
  if (raw == null) return null;
  let t = raw.trim();
  try {
    t = decodeURIComponent(t);
  } catch {
    return null;
  }
  if (!t.startsWith('/') || t.startsWith('//')) return null;
  if (/[\r\n\u0000]/.test(t)) return null;
  const pathPart = t.split('?')[0];
  if (pathPart.includes(':')) return null;
  return t;
}

/** Helper for returnTo navigation. */
@Injectable({ providedIn: 'root' })
export class NavigationService {
  constructor(private router: Router) {}

  getReturnTo(route: ActivatedRoute): string | null {
    return sanitizeInternalReturnTo(route.snapshot.queryParamMap.get('returnTo'));
  }

  navigateBack(route: ActivatedRoute, defaultPath: string): void {
    const returnTo = this.getReturnTo(route);
    this.router.navigateByUrl(returnTo || defaultPath);
  }

  linkWithReturnTo(route: ActivatedRoute, segments: string[], queryParams: Record<string, string> = {}): { link: string[]; queryParams: Record<string, string> } {
    const returnTo = this.getReturnTo(route);
    if (returnTo) queryParams = { ...queryParams, returnTo };
    return { link: segments, queryParams };
  }
}
