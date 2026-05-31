import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withPreloading } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';
import { httpErrorInterceptor } from './core/interceptors/http-error.interceptor';
import { loadingInterceptor } from './core/interceptors/loading.interceptor';
import { AuthPreloadingStrategy } from './core/routing/auth-preloading.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withPreloading(AuthPreloadingStrategy)),
    provideHttpClient(withInterceptors([jwtInterceptor, loadingInterceptor, httpErrorInterceptor]))
  ]
};
