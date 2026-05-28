import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs/operators';
import { LoadingIndicatorService } from '../services/loading-indicator.service';

export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  // Optional escape hatch for requests that should not trigger global loading.
  if (req.headers.has('x-skip-loader')) {
    return next(req);
  }

  const loader = inject(LoadingIndicatorService);
  loader.begin();
  return next(req).pipe(finalize(() => loader.end()));
};
