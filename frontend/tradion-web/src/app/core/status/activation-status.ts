export const ActivationFilterStatus = {
  all: 'all',
  active: 'active',
  inactive: 'inactive'
} as const;

export type ActivationFilterStatusType =
  (typeof ActivationFilterStatus)[keyof typeof ActivationFilterStatus];
