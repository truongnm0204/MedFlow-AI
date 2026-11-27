import { describe, expect, it } from 'vitest';

import {
  confirmRecoverySchema,
  createRecoveryRequestSchema,
  grantRecoverySchema,
  recoveryDecisionSchema,
} from '../schemas/clinical-recovery.schema';

describe('clinical recovery schemas', () => {
  it('accepts a request payload carrying the queue state version from the context', () => {
    expect(
      createRecoveryRequestSchema.parse({
        queueEntryId: 801,
        expectedStateVersion: 8,
        reason: 'Bác sĩ phụ trách nghỉ phép, phiên chưa được giải phóng.',
      }),
    ).toEqual({
      queueEntryId: 801,
      expectedStateVersion: 8,
      reason: 'Bác sĩ phụ trách nghỉ phép, phiên chưa được giải phóng.',
    });
  });

  it('rejects reasons outside the backend 3..500 window', () => {
    const base = { queueEntryId: 801, expectedStateVersion: 8 };

    expect(createRecoveryRequestSchema.safeParse({ ...base, reason: 'ab' }).success).toBe(false);
    expect(
      createRecoveryRequestSchema.safeParse({ ...base, reason: 'a'.repeat(501) }).success,
    ).toBe(false);
  });

  it('rejects markup characters in the reason', () => {
    const result = createRecoveryRequestSchema.safeParse({
      queueEntryId: 801,
      expectedStateVersion: 8,
      reason: 'Phiên gián đoạn <script>',
    });

    expect(result.success).toBe(false);
  });

  it('requires a positive authorized doctor id for a grant', () => {
    const base = { expectedStateVersion: 8, reason: 'Cấp cho bác sĩ cùng chuyên khoa.' };

    expect(grantRecoverySchema.safeParse({ ...base, authorizedDoctorId: 0 }).success).toBe(false);
    expect(grantRecoverySchema.safeParse({ ...base, authorizedDoctorId: 25 }).success).toBe(true);
  });

  it('refuses a confirm that does not attest the interruption', () => {
    const payload = {
      expectedStateVersion: 8,
      confirmInterrupted: false,
      reason: 'Không còn được thực hiện trực tiếp.',
    };

    expect(confirmRecoverySchema.safeParse(payload).success).toBe(false);
    expect(confirmRecoverySchema.safeParse({ ...payload, confirmInterrupted: true }).success).toBe(
      true,
    );
  });

  it('keeps the state version optional for reject, revoke and cancel', () => {
    expect(recoveryDecisionSchema.parse({ reason: 'Phiên vẫn đang được thực hiện.' })).toEqual({
      reason: 'Phiên vẫn đang được thực hiện.',
    });
  });
});
