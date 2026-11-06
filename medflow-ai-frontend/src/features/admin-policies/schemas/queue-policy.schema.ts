import { z } from 'zod';

import { policyIntField } from './policy-int-field';

export const queuePolicySchema = z.object({
  graceMinutes: policyIntField('Thời gian ân hạn đúng giờ', 0, 60),
  expectedDurationMinutes: policyIntField('Thời lượng khám dự kiến', 5, 120),
  safetyBufferMinutes: policyIntField('Thời gian dự phòng', 0, 30),
  walkinPriorityRatio: z.object({
    priority: policyIntField('Số lượt ưu tiên liên tiếp', 1, 10),
    normal: policyIntField('Số lượt thường xen kẽ', 1, 10),
  }),
  publicNumberImmutable: z.boolean(),
  manualPriorityRequiresVerification: z.boolean(),
});

export type QueuePolicyFormValues = z.infer<typeof queuePolicySchema>;
