export const InvoiceStatus = {
  paid: 'paid'
} as const;

export function normalizeInvoiceStatus(status?: string | null): string {
  return (status || '').trim().toLowerCase();
}

export function isInvoicePaid(status?: string | null): boolean {
  return normalizeInvoiceStatus(status) === InvoiceStatus.paid;
}
