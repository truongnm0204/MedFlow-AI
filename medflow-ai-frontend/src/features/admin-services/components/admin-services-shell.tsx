import type { ReactNode } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

interface AdminServicesShellProps {
  children: ReactNode;
}

export function AdminServicesShell({ children }: AdminServicesShellProps) {
  return (
    <AdminPortalShell
      showTabs={true}
      activeTab="Danh mục Dịch vụ & Kỹ thuật"
      breadcrumbText="Danh mục Dịch vụ Khám Ngoại trú (SCR-ADM-ESV-01)"
      breadcrumbLink="/admin/services"
    >
      {children}
    </AdminPortalShell>
  );
}
