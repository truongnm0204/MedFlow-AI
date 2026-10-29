import type { ReactNode } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

interface AdminReportsShellProps {
  children: ReactNode;
}

/** Shared hospital administration chrome for SCR-ADM-RPT-01 Operational Reports. */
export function AdminReportsShell({ children }: AdminReportsShellProps) {
  return (
    <AdminPortalShell
      activeTab="Báo cáo vận hành"
      breadcrumbText="Báo cáo vận hành"
      breadcrumbLink="/admin/reports"
    >
      {children}
    </AdminPortalShell>
  );
}
