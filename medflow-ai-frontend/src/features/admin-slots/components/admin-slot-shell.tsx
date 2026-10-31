import type { ReactNode } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

interface AdminSlotShellProps {
  children: ReactNode;
}

export function AdminSlotShell({ children }: AdminSlotShellProps) {
  return (
    <AdminPortalShell
      showTabs={true}
      activeTab="Quản lý Slot khám"
      breadcrumbText="Quản lý khung giờ khám (SCR-ADM-SLT-01)"
      breadcrumbLink="/admin/slots"
    >
      {children}
    </AdminPortalShell>
  );
}
