export const JobStatus = {
  draft: 'draft',
  open: 'open',
  inProgress: 'in progress',
  inProgressCompact: 'inprogress',
  completed: 'completed',
  done: 'done',
  closed: 'closed'
} as const;

export function normalizeJobStatus(status?: string | null): string {
  return (status || '').trim().toLowerCase();
}

export function isJobDraftLike(status?: string | null): boolean {
  return normalizeJobStatus(status) === JobStatus.draft;
}

export function isJobOpenLike(status?: string | null): boolean {
  return normalizeJobStatus(status) === JobStatus.open;
}

export function isJobInProgressLike(status?: string | null): boolean {
  const s = normalizeJobStatus(status).replace(/\s+/g, '');
  return s === JobStatus.inProgressCompact;
}

export function isJobCompletedLike(status?: string | null): boolean {
  const s = normalizeJobStatus(status);
  return s === JobStatus.completed || s === JobStatus.done || s === JobStatus.closed;
}
