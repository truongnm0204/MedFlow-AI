import { FlaskConical, Stethoscope } from 'lucide-react';
import { NavLink } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { hasAnyRole } from '@/features/auth/utils/rbac';

export function ServiceCatalogTabs() {
  const roles = useAuthStore((state) => state.user?.roles);
  const canViewDiagnosticCatalog = hasAnyRole(roles, ['HOSPITAL_ADMIN']);

  return (
    <nav
      aria-label="Danh mục dịch vụ"
      className="flex gap-4 border-b border-border bg-white px-6 pt-4"
    >
      <NavLink
        to="/admin/services"
        end={false}
        className={({ isActive }) =>
          `inline-flex items-center gap-2 pb-3 text-xs font-semibold transition-colors ${
            isActive ? 'border-b-2 border-primary text-primary' : 'text-sub hover:text-ink'
          }`
        }
      >
        <Stethoscope aria-hidden="true" className="h-4 w-4" />
        <span>Dịch vụ khám</span>
      </NavLink>

      {canViewDiagnosticCatalog && (
        <NavLink
          to="/admin/diagnostic-services"
          className={({ isActive }) =>
            `inline-flex items-center gap-2 pb-3 text-xs font-semibold transition-colors ${
              isActive ? 'border-b-2 border-primary text-primary' : 'text-sub hover:text-ink'
            }`
          }
        >
          <FlaskConical aria-hidden="true" className="h-4 w-4" />
          <span>Dịch vụ cận lâm sàng</span>
        </NavLink>
      )}
    </nav>
  );
}
