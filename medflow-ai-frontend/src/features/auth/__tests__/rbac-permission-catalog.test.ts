import { describe, expect, it } from 'vitest';

import { SYSTEM_PERMISSIONS } from '@/features/admin-roles/data/role-mock-data';
import { ROLE_DEFAULT_PERMISSIONS, SYSTEM_PERMISSION_CODES } from '../utils/rbac';

/**
 * The admin catalog (role-mock-data) and the client-side gate (rbac) duplicate the permission
 * list. They drifted once (QUEUE_ENROLL, PAYMENT_COLLECT, PATIENT_IDENTITY_VERIFY were missing on
 * one side), so keep them locked together here.
 */
describe('permission catalog parity', () => {
  it('keeps the rbac code list and the admin catalog identical', () => {
    const rbacCodes = [...SYSTEM_PERMISSION_CODES].sort();
    const catalogCodes = Object.keys(SYSTEM_PERMISSIONS).sort();

    expect(catalogCodes).toStrictEqual(rbacCodes);
  });

  it('never grants clinical recovery permissions by role default', () => {
    const granted = new Set(Object.values(ROLE_DEFAULT_PERMISSIONS).flat());

    expect(granted.has('CLINICAL_SESSION_RECOVERY_GRANT')).toBe(false);
    expect(granted.has('CLINICAL_SESSION_RECOVER')).toBe(false);
  });

  it('exports every recovery permission the backend authorizes', () => {
    expect(SYSTEM_PERMISSION_CODES).toContain('CLINICAL_SESSION_RECOVERY_GRANT');
    expect(SYSTEM_PERMISSION_CODES).toContain('CLINICAL_SESSION_RECOVER');
    expect(SYSTEM_PERMISSION_CODES).toContain('QUEUE_ENROLL');
  });
});
