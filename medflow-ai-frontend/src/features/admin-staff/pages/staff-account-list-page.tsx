import { useState } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { DeactivateStaffModal } from '../components/deactivate-staff-modal';
import type { StaffAccountStatus, StaffTargetSummary } from '../types/deactivate-staff.types';

const INITIAL_STAFF: StaffTargetSummary[] = [
  {
    id: 'stf-0142',
    staffCode: 'STF-0142',
    fullName: 'Lê Hoàng Minh',
    role: 'Bác sĩ',
    department: 'Khoa Nội Tiêu hóa',
    email: 'minh.lh@medflow.vn',
    pendingAssignments: 0,
  },
  {
    id: 'stf-0150',
    staffCode: 'STF-0150',
    fullName: 'Trần Thu Hà',
    role: 'Điều dưỡng',
    department: 'Khoa Nội Tiêu hóa',
    email: 'ha.tt@medflow.vn',
    pendingAssignments: 3,
  },
];

export function StaffAccountListPage() {
  const [statuses, setStatuses] = useState<Record<string, StaffAccountStatus>>({});
  const [target, setTarget] = useState<StaffTargetSummary | null>(null);

  return (
    <AdminPortalShell
      showTabs={false}
      activeTab="Bác sĩ & Nhân sự Y tế"
      breadcrumbText="Tài khoản nhân sự"
      breadcrumbCode="SCR-ADM-DOC-05"
      breadcrumbLink="/admin/staff"
    >
      <div className="p-6">
        <h1 className="mb-4 text-lg font-bold text-navy">Tài khoản nhân sự</h1>
        <table className="w-full text-left text-sm">
          <thead className="text-xs uppercase text-sub">
            <tr>
              <th className="py-2">Mã nhân sự</th>
              <th className="py-2">Họ và tên</th>
              <th className="py-2">Trạng thái</th>
              <th className="py-2" />
            </tr>
          </thead>
          <tbody>
            {INITIAL_STAFF.map((staff) => {
              const status = statuses[staff.id] ?? 'ACTIVE';
              return (
                <tr key={staff.id} className="border-t border-border">
                  <td className="py-2 font-mono">{staff.staffCode}</td>
                  <td className="py-2 text-ink">{staff.fullName}</td>
                  <td className="py-2">{status}</td>
                  <td className="py-2 text-right">
                    <button
                      type="button"
                      disabled={status === 'DISABLED'}
                      onClick={() => setTarget(staff)}
                      aria-label={`Ngừng hoạt động ${staff.fullName}`}
                      className="text-sm font-semibold text-red-600 disabled:opacity-40"
                    >
                      Ngừng hoạt động
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {target && (
        <DeactivateStaffModal
          staff={target}
          onCancel={() => setTarget(null)}
          onConfirm={({ staffId }) => {
            setStatuses((prev) => ({ ...prev, [staffId]: 'DISABLED' }));
            setTarget(null);
          }}
        />
      )}
    </AdminPortalShell>
  );
}
