import type { UpdateQueuePolicyRequest } from '../types/queue-policy.types';

export const DEFAULT_QUEUE_POLICY_VALUES: UpdateQueuePolicyRequest = {
  graceMinutes: 15,
  expectedDurationMinutes: 20,
  safetyBufferMinutes: 5,
  walkinPriorityRatio: {
    priority: 3,
    normal: 1,
  },
  publicNumberImmutable: true,
  manualPriorityRequiresVerification: true,
};
