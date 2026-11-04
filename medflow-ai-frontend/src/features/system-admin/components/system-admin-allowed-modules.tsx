import type { LucideIcon } from 'lucide-react';
import {
  ArrowUpRight,
  Building2,
  FileSearch,
  ShieldCheck,
  SlidersHorizontal,
  UserCog,
} from 'lucide-react';
import { Link } from 'react-router-dom';

import { Badge } from '@/shared/ui/badge';

import type { SystemAdminAllowedModuleDto } from '../types/system-admin.types';

interface SystemAdminAllowedModulesProps {
  modules: SystemAdminAllowedModuleDto[];
  permissions: string[];
  selectedModuleCode?: string;
  onSelectModule?: (module: SystemAdminAllowedModuleDto) => void;
}

const MODULE_META: Record<string, { icon: LucideIcon; ucCode: string; accent: string }> = {
  STAFF_ACCOUNTS: {
    icon: UserCog,
    ucCode: 'UC57',
    accent: 'border-sky-200 bg-sky-50 text-sky-700',
  },
  ROLES_PERMISSIONS: {
    icon: ShieldCheck,
    ucCode: 'UC58',
    accent: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  },
  HOSPITAL_DEPARTMENTS: {
    icon: Building2,
    ucCode: 'UC47',
    accent: 'border-amber-200 bg-amber-50 text-amber-700',
  },
  APPOINTMENT_POLICIES: {
    icon: SlidersHorizontal,
    ucCode: 'UC59',
    accent: 'border-sky-200 bg-sky-50 text-sky-700',
  },
  AUDIT_LOGS: {
    icon: FileSearch,
    ucCode: 'UC60',
    accent: 'border-rose-200 bg-rose-50 text-rose-700',
  },
};

export function SystemAdminAllowedModules({
  modules,
  permissions,
  selectedModuleCode,
  onSelectModule,
}: SystemAdminAllowedModulesProps) {
  const normalizedPermissions = new Set(permissions.map((p) => p.toUpperCase()));
  const permittedModules =
    normalizedPermissions.size > 0
      ? modules.filter((m) => normalizedPermissions.has(m.requiredPermission.toUpperCase()))
      : modules;

  if (permittedModules.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-canvas px-4 py-8 text-center text-xs font-medium text-sub">
        Tài khoản hiện chưa được cấp quyền truy cập phân hệ quản trị nào.
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {permittedModules.map((mod) => {
        const meta = MODULE_META[mod.moduleCode] ?? {
          icon: ShieldCheck,
          ucCode: 'UC-SYS',
          accent: 'border-sky-200 bg-sky-50 text-sky-700',
        };
        const Icon = meta.icon;
        const isSelected = selectedModuleCode === mod.moduleCode;

        return (
          <div
            key={mod.moduleCode}
            onClick={() => onSelectModule?.(mod)}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onSelectModule?.(mod);
              }
            }}
            className={`group flex cursor-pointer flex-col justify-between rounded-2xl border p-4 text-left transition-all focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none ${
              isSelected
                ? 'border-primary bg-sky-50/50 shadow-sm'
                : 'border-border bg-white hover:border-slate-300 hover:shadow-2xs'
            }`}
          >
            <div>
              <div className="mb-3 flex items-center justify-between gap-2">
                <div
                  className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border ${meta.accent}`}
                >
                  <Icon aria-hidden="true" className="h-4 w-4" />
                </div>
                <div className="flex flex-wrap items-center gap-1">
                  <span className="rounded border border-sky-200 bg-sky-50 px-1.5 py-0.5 font-mono text-[10px] font-bold text-primary">
                    {meta.ucCode}
                  </span>
                  <Badge tone="slate">{mod.requiredPermission}</Badge>
                </div>
              </div>

              <div className="text-xs font-bold text-ink">{mod.title}</div>
              <p className="mt-1 text-[11px] leading-snug text-sub">{mod.description}</p>
            </div>

            <div className="mt-3.5 flex items-center justify-between border-t border-slate-100 pt-2.5">
              <span className="font-mono text-[11px] font-semibold text-sub">
                {mod.badgeCount !== null ? `${mod.badgeCount} bản ghi` : 'Sẵn sàng'}
              </span>
              <Link
                to={mod.route}
                onClick={(e) => e.stopPropagation()}
                className="inline-flex items-center gap-1 rounded-lg border border-border bg-white px-2.5 py-1 text-[11px] font-semibold text-primary transition-colors hover:border-sky-300 hover:bg-sky-50"
              >
                <span>Mở phân hệ</span>
                <ArrowUpRight aria-hidden="true" className="h-3.5 w-3.5" />
              </Link>
            </div>
          </div>
        );
      })}
    </div>
  );
}
