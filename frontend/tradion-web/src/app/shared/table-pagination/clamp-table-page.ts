/** Keeps current page valid when the number of rows changes (e.g. after load or filter). */
export function clampTablePage(page: number, itemCount: number, pageSize: number): number {
  const totalPages = Math.max(1, Math.ceil(itemCount / pageSize));
  return Math.min(Math.max(1, page), totalPages);
}
