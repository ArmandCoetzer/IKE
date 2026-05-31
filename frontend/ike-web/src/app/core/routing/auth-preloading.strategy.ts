import { Injectable } from '@angular/core';
import { PreloadingStrategy, Route } from '@angular/router';
import { Observable, of } from 'rxjs';
import { AuthService } from '../services/auth.service';

@Injectable({ providedIn: 'root' })
export class AuthPreloadingStrategy implements PreloadingStrategy {
  constructor(private auth: AuthService) {}

  preload(_route: Route, load: () => Observable<unknown>): Observable<unknown> {
    return this.auth.getToken() && !this.auth.isTokenExpired() ? load() : of(null);
  }
}
