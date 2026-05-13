export const PermitStatus = {
  draft: 'draft',
  captured: 'captured',
  signed: 'signed',
  active: 'active',
  approved: 'approved',
  expired: 'expired',
  closed: 'closed',
  done: 'done',
  rejected: 'rejected',
  cancelled: 'cancelled'
} as const;

export function normalizePermitStatus(status?: string | null): string {
  return (status || '').trim().toLowerCase();
}

export function isPermitDraftLike(status?: string | null): boolean {
  return normalizePermitStatus(status) === PermitStatus.draft;
}

export function isPermitCapturedLike(status?: string | null): boolean {
  const s = normalizePermitStatus(status);
  return s === PermitStatus.captured || s === PermitStatus.signed;
}

export function isPermitActiveLike(status?: string | null): boolean {
  const s = normalizePermitStatus(status);
  return s === PermitStatus.active || s === PermitStatus.approved;
}

export function isPermitExpiredLike(status?: string | null): boolean {
  return normalizePermitStatus(status) === PermitStatus.expired;
}

export function isPermitClosedLike(status?: string | null): boolean {
  const s = normalizePermitStatus(status);
  return s === PermitStatus.closed || s === PermitStatus.done;
}

export function isPermitRejectedOrCancelled(status?: string | null): boolean {
  const s = normalizePermitStatus(status);
  return s === PermitStatus.rejected || s === PermitStatus.cancelled;
}
