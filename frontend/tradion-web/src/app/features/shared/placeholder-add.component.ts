import { Component } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  selector: 'app-placeholder-add',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="container-fluid">
      <div class="d-flex align-items-center gap-2 mb-4">
        <a [routerLink]="backLink()" class="btn btn-light border btn-sm">← Back</a>
        <h1 class="mb-0 fw-bold">{{ title() }}</h1>
      </div>
      <div class="card border-0 shadow-sm rounded-3">
        <div class="card-body p-4">
          <p class="text-muted mb-0">{{ message() }}</p>
        </div>
      </div>
    </div>
  `
})
export class PlaceholderAddComponent {
  constructor(private route: ActivatedRoute) {}

  title() {
    return this.route.snapshot.data['title'] ?? 'Add';
  }

  backLink() {
    return this.route.snapshot.data['backLink'] ?? '/';
  }

  message() {
    return this.route.snapshot.data['message'] ?? 'Form coming soon.';
  }
}
