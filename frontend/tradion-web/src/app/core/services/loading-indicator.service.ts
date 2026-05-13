import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class LoadingIndicatorService {
  private activeRequests = signal(0);
  readonly isLoading = computed(() => this.activeRequests() > 0);

  begin(): void {
    this.activeRequests.update((count) => count + 1);
  }

  end(): void {
    this.activeRequests.update((count) => (count > 0 ? count - 1 : 0));
  }
}
