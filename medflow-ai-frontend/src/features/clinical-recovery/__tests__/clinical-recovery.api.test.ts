import { beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));

vi.mock('@/shared/lib/axios', () => ({ apiClient: { get: api.get, post: api.post } }));

import {
  cancelClinicalRecoveryApi,
  confirmClinicalRecoveryApi,
  createClinicalRecoveryRequestApi,
  fetchClinicalRecoveryContextApi,
  fetchClinicalRecoveryEligibleRecipientsApi,
  fetchClinicalRecoveryListApi,
  grantClinicalRecoveryApi,
  rejectClinicalRecoveryApi,
  revokeClinicalRecoveryApi,
} from '../api/clinical-recovery.api';

describe('clinical recovery API paths', () => {
  beforeEach(() => {
    api.get.mockReset().mockResolvedValue({ data: {} });
    api.post.mockReset().mockResolvedValue({ data: {} });
  });

  it('loads the visit-scoped context and encodes the visit code', async () => {
    await fetchClinicalRecoveryContextApi('  VISIT 1  ');

    expect(api.get).toHaveBeenCalledWith('/visits/VISIT%201/clinical-session-recovery-context');
  });

  it('lists scoped requests and eligible recipients (Commit B endpoints)', async () => {
    await fetchClinicalRecoveryListApi();
    await fetchClinicalRecoveryEligibleRecipientsApi(101);

    expect(api.get.mock.calls.map((call) => call[0])).toStrictEqual([
      '/clinical-session-recovery-requests',
      '/clinical-session-recovery-requests/101/eligible-recipients',
    ]);
  });
  it('creates a request under the visit route', async () => {
    const payload = { queueEntryId: 801, expectedStateVersion: 8, reason: 'Gián đoạn.' };

    await createClinicalRecoveryRequestApi('VISIT-1', payload);

    expect(api.post).toHaveBeenCalledWith(
      '/visits/VISIT-1/clinical-session-recovery-requests',
      payload,
    );
  });

  it('targets the request id for grant, reject, revoke, cancel and confirm', async () => {
    const decision = { reason: 'Không đủ căn cứ.' };

    await grantClinicalRecoveryApi(101, {
      authorizedDoctorId: 25,
      expectedStateVersion: 8,
      reason: 'Cấp cho bác sĩ cùng khoa.',
    });
    await rejectClinicalRecoveryApi(101, decision);
    await revokeClinicalRecoveryApi(101, decision);
    await cancelClinicalRecoveryApi(101, decision);
    await confirmClinicalRecoveryApi(101, {
      expectedStateVersion: 8,
      confirmInterrupted: true,
      reason: 'Đã xác minh phiên bị gián đoạn.',
    });

    expect(api.post.mock.calls.map((call) => call[0])).toStrictEqual([
      '/clinical-session-recovery-requests/101/grant',
      '/clinical-session-recovery-requests/101/reject',
      '/clinical-session-recovery-requests/101/revoke',
      '/clinical-session-recovery-requests/101/cancel',
      '/clinical-session-recovery-requests/101/confirm',
    ]);
  });
});
