import type { ReactNode } from 'react';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { ServiceCatalogTabs } from './service-catalog-tabs';

interface AdminServicesShellProps {
  variant: 'examination' | 'diagnostic';
  children: ReactNode;
}

export function AdminServicesShell({ variant, children }: AdminServicesShellProps) {
  const breadcrumbText =
    variant === 'diagnostic'
      ? 'Danh mục Dịch vụ Cận lâm sàng (SCR-ADM-DSV-01)'
      : 'Danh mục Dịch vụ Khám Ngoại trú (SCR-ADM-ESV-01)';
  const breadcrumbLink =
    variant === 'diagnostic' ? '/admin/diagnostic-services' : '/admin/services';

  return (
    <AdminPortalShell
      showTabs={true}
      activeTab="Danh mục Dịch vụ & Kỹ thuật"
      breadcrumbText={breadcrumbText}
      breadcrumbLink={breadcrumbLink}
    >
      <ServiceCatalogTabs />
      {children}
    </AdminPortalShell>
  );
}
