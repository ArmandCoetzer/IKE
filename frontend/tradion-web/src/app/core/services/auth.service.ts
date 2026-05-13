import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_BASE } from '../api-url';
import { sanitizeInternalReturnTo } from './navigation.service';

const API = `${API_BASE}/auth`;
const TOKEN_KEY = 'tradion_token';
const TOKEN_PERSIST_KEY = 'tradion_token_persist';
const RETURN_URL_KEY = 'tradion_return_url';

export interface AuthResponse {
  token: string;
  expiresAt: string;
  email: string;
  role?: string;
  permissions: string[];
  fullName?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export interface RegisterRequest {
  email: string;
  password: string;
  fullName?: string;
  companyName: string;
  companyAddress?: string;
  companyPhone?: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export interface ProfileDto {
  userId: string;
  email: string;
  fullName?: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
}

export interface UpdateProfileRequest {
  email?: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private token = signal<string | null>(this.getStoredToken());
  private authResponse = signal<AuthResponse | null>(null);
  private logoutTimer: ReturnType<typeof setTimeout> | null = null;

  readonly isLoggedIn = computed(() => !!this.token());
  readonly user = computed(() => this.authResponse());
  readonly role = computed(() => this.authResponse()?.role ?? null);
  readonly permissions = computed(() => this.authResponse()?.permissions ?? []);

  constructor(
    private http: HttpClient,
    private router: Router
  ) {
    this.initializeSession();
  }

  getToken(): string | null {
    return this.token();
  }

  private getStoredToken(): string | null {
    return localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
  }

  private shouldPersistToken(): boolean {
    return localStorage.getItem(TOKEN_PERSIST_KEY) === '1';
  }

  private setPersistPreference(rememberMe: boolean): void {
    localStorage.setItem(TOKEN_PERSIST_KEY, rememberMe ? '1' : '0');
  }

  private decodeTokenPayload(token: string): Record<string, unknown> | null {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) return null;
      const payload = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const normalized = payload.padEnd(Math.ceil(payload.length / 4) * 4, '=');
      return JSON.parse(atob(normalized));
    } catch {
      return null;
    }
  }

  isTokenExpired(token = this.token()): boolean {
    if (!token) return true;
    const payload = this.decodeTokenPayload(token);
    const exp = typeof payload?.['exp'] === 'number' ? payload['exp'] : Number(payload?.['exp']);
    if (!Number.isFinite(exp)) return true;
    return Date.now() >= exp * 1000;
  }

  private clearLogoutTimer(): void {
    if (this.logoutTimer) {
      clearTimeout(this.logoutTimer);
      this.logoutTimer = null;
    }
  }

  private scheduleLogoutFromToken(token: string): void {
    this.clearLogoutTimer();
    const payload = this.decodeTokenPayload(token);
    const exp = typeof payload?.['exp'] === 'number' ? payload['exp'] : Number(payload?.['exp']);
    if (!Number.isFinite(exp)) {
      this.logout();
      return;
    }
    const msUntilExpiry = exp * 1000 - Date.now();
    if (msUntilExpiry <= 0) {
      this.logout();
      return;
    }
    this.logoutTimer = setTimeout(() => this.logout(), msUntilExpiry);
  }

  private applyAuthResponse(res: AuthResponse): void {
    this.authResponse.set(res);
    if (!res.token) return;
    if (this.shouldPersistToken()) {
      localStorage.setItem(TOKEN_KEY, res.token);
      sessionStorage.removeItem(TOKEN_KEY);
    } else {
      sessionStorage.setItem(TOKEN_KEY, res.token);
      localStorage.removeItem(TOKEN_KEY);
    }
    this.token.set(res.token);
    this.scheduleLogoutFromToken(res.token);
  }

  initializeSession(): void {
    const stored = this.getStoredToken();
    if (!stored) return;
    if (this.isTokenExpired(stored)) {
      this.logout();
      return;
    }
    this.token.set(stored);
    this.scheduleLogoutFromToken(stored);
  }

  login(req: LoginRequest): Observable<AuthResponse> {
    this.setPersistPreference(!!req.rememberMe);
    return this.http.post<AuthResponse>(`${API}/login-web`, req).pipe(
      tap((res) => this.applyAuthResponse(res))
    );
  }

  register(req: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${API}/register`, req).pipe(
      tap((res) => this.applyAuthResponse(res))
    );
  }

  me(): Observable<AuthResponse> {
    return this.http.get<AuthResponse>(`${API}/me`).pipe(
      tap((res) => this.applyAuthResponse(res))
    );
  }

  forgotPassword(req: ForgotPasswordRequest): Observable<void> {
    return this.http.post<void>(`${API}/forgot-password`, req);
  }

  resetPassword(req: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${API}/reset-password`, req);
  }

  getProfile(): Observable<ProfileDto> {
    return this.http.get<ProfileDto>(`${API}/profile`);
  }

  updateProfile(req: UpdateProfileRequest): Observable<ProfileDto> {
    return this.http.put<ProfileDto>(`${API}/profile`, req);
  }

  changePassword(req: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(`${API}/change-password`, req);
  }

  setReturnUrl(url: string): void {
    const safe = sanitizeInternalReturnTo(url);
    if (!safe) return;
    localStorage.setItem(RETURN_URL_KEY, safe);
  }

  consumeReturnUrl(): string | null {
    const url = localStorage.getItem(RETURN_URL_KEY);
    localStorage.removeItem(RETURN_URL_KEY);
    return sanitizeInternalReturnTo(url);
  }

  logout(): void {
    this.clearLogoutTimer();
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    this.authResponse.set(null);
    if (this.router.url !== '/login') {
      this.router.navigate(['/login']);
    }
  }

  hasPermission(name: string): boolean {
    return this.permissions().includes(name);
  }

  /** User id from JWT (sub / nameidentifier), when present. */
  jwtUserId(): string | null {
    const t = this.token();
    if (!t) return null;
    const p = this.decodeTokenPayload(t);
    const sub = p?.['sub'] ?? p?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
    return typeof sub === 'string' && sub.length > 0 ? sub : null;
  }
}
