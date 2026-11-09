export interface WalkinPriorityRatio {
  priority: number;
  normal: number;
}

export interface QueuePolicyDto {
  graceMinutes: number;
  expectedDurationMinutes: number;
  safetyBufferMinutes: number;
  walkinPriorityRatio: WalkinPriorityRatio;
  publicNumberImmutable: boolean;
  manualPriorityRequiresVerification: boolean;
  lastUpdatedAt: string | null;
}

export type UpdateQueuePolicyRequest = Omit<QueuePolicyDto, 'lastUpdatedAt'>;
