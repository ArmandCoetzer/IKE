import { environment } from '../../environments/environment';

function getApiBase(): string {
  let base = environment.apiUrl ?? '';
  if (base) {
    const normalized = base.trim().replace(/\/+$/, '');
    return normalized.endsWith('/api') ? normalized : `${normalized}/api`;
  }
  if (typeof window !== 'undefined') {
    const host = window.location.hostname;
    const protocol = window.location.protocol || 'http:';
    // Dev fallback only. Production must use environment.prod.ts to avoid mixed-content/cross-host mistakes.
    base = host !== 'localhost' && host !== '127.0.0.1'
      ? `${protocol}//${host}:5020`
      : 'http://localhost:5020';
  }
  return `${base.replace(/\/+$/, '')}/api`;
}

export const API_BASE = getApiBase();
