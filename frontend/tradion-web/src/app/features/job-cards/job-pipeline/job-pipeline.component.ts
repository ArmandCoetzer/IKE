import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { isJobCompletedLike, isJobInProgressLike } from '../../../core/status/job-status';

export interface JobPipelineData {
  status: string;
  documents: { purchaseOrderId?: string }[];
  parts: unknown[];
  permits: unknown[];
}

const STAGES = [
  'Request',
  'Quote',
  'PO',
  'Job card',
  'Permits',
  'Work',
  'Invoice',
  'Paid'
];

@Component({
  selector: 'app-job-pipeline',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="job-pipeline border rounded p-2 mb-3 bg-light">
      <div class="d-flex flex-wrap align-items-center gap-1 small" aria-label="Workflow stages">
        @for (s of stages; track s.name; let i = $index) {
          <span
            class="badge text-nowrap"
            [class.bg-secondary]="i < currentIndex"
            [class.bg-primary]="i === currentIndex"
            [class.bg-light]="i > currentIndex"
            [class.text-dark]="i > currentIndex"
          >
            {{ s.name }}
          </span>
          @if (i < stages.length - 1) {
            <span class="text-muted" aria-hidden="true">→</span>
          }
        }
      </div>
      @if (nextAction()) {
        <p class="mb-0 mt-2 small text-muted">{{ nextAction() }}</p>
      }
    </div>
  `,
  styles: [`
    .job-pipeline .badge { font-weight: 500; }
  `]
})
export class JobPipelineComponent {
  job = input.required<JobPipelineData>();

  get stages(): { name: string }[] {
    return STAGES.map(name => ({ name }));
  }

  get currentIndex(): number {
    const j = this.job();
    if (!j) return 3;
    if (isJobCompletedLike(j.status)) return 6; // Invoice
    if (isJobInProgressLike(j.status) || (j.status || '').toLowerCase().includes('work')) return 5; // Work
    const hasParts = (j.parts?.length ?? 0) > 0;
    const hasPermits = (j.permits?.length ?? 0) > 0;
    if (hasParts && hasPermits) return 5; // Work
    if (hasPermits) return 4; // Permits
    if (hasParts) return 4; // Permits (parts planned; permits next)
    return 3; // Job card (default)
  }

  nextAction(): string {
    const i = this.currentIndex;
    if (i <= 3) return 'Add permits and planned parts as needed, then start work.';
    if (i <= 4) return 'Complete permits and work.';
    if (i <= 5) return 'Mark work complete and create invoice.';
    if (i <= 6) return 'Send invoice or mark as paid.';
    return '';
  }
}
