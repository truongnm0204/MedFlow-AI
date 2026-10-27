import type { ReactNode } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

interface AdminDoctorShellProps {
  children: ReactNode;
  showTabs?: boolean;
}

/** Shared hospital administration chrome from the doctor Figma frames. */
export function AdminDoctorShell({ children, showTabs = false }: AdminDoctorShellProps) {
  return (
    <AdminPortalShell
      showTabs={showTabs}
      activeTab="Bác sĩ & Nhân sự Y tế"
      breadcrumbText="Danh mục Bác sĩ"
      breadcrumbCode="SCR-ADM-DOC"
      breadcrumbLink="/admin/doctors"
    >
      {children}
    </AdminPortalShell>
  );
}
