import { environment } from '../../environments/environment';

function getApiBase(): string {
  let base = environment.apiUrl ?? '';
  if (base) {
    const normalized = base.trim().replace(/\/+$/, '');
    return normalized.endsWith('/api') ? normalized : `${normalized}/api`;
  }
  if (typeof window !== 'undefined') {
    const host = window.location.hostname;
    // Dev: use same host as frontend with API port (e.g. 192.168.10.161:4200 -> 192.168.10.161:5020)
    if (host !== 'localhost' && host !== '127.0.0.1') {
      base = `http://${host}:5020`;
    } else {
      base = 'http://localhost:5020';
    }
  }
  return `${base.replace(/\/+$/, '')}/api`;
}

export const API_BASE = getApiBase();
