import { Check, Minus, Shield } from 'lucide-react';
import { Fragment } from 'react';

import { PERMISSION_GROUPS } from '../data/role-mock-data';
import type { RoleItem } from '../types/role.types';

export interface RolePermissionMatrixViewProps {
  roles: RoleItem[];
  canAssignPermissions: boolean;
  onSelectRoleForAssign: (role: RoleItem) => void;
}

export function RolePermissionMatrixView({
  roles,
  canAssignPermissions,
  onSelectRoleForAssign,
}: RolePermissionMatrixViewProps) {
  if (roles.length === 0) {
    return (
      <div className="rounded-xl border border-border bg-white px-4 py-10 text-center text-sm text-sub shadow-sm">
        Không tìm thấy vai trò hệ thống phù hợp để hiển thị ma trận quyền.
      </div>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-white shadow-sm">
      <div className="overflow-x-auto">
        <table
          aria-label="Bảng ma trận phân quyền vai trò"
          className="w-full border-collapse text-left text-sm"
        >
          <thead className="bg-canvas text-xs font-semibold tracking-wide text-sub">
            <tr className="border-b border-border">
              <th scope="col" className="min-w-[240px] px-4 py-3 text-left">
                QUYỀN HẠN / PHÂN HỆ
              </th>
              {roles.map((role) => (
                <th
                  key={role.id}
                  scope="col"
                  className="min-w-[140px] border-l border-border px-3 py-3 text-center align-top"
                >
                  <div className="flex flex-col items-center gap-1">
                    <span className="font-mono text-xs font-bold text-navy">{role.code}</span>
                    <span className="text-[11px] font-medium text-ink">{role.name}</span>
                    <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-sub">
                      {role.permissions.length} quyền
                    </span>
                    {canAssignPermissions && (
                      <button
                        type="button"
                        onClick={() => onSelectRoleForAssign(role)}
                        aria-label={`Phân quyền ${role.name}`}
                        className="mt-1 inline-flex items-center gap-1 rounded-md border border-sky-200 bg-sky-50 px-2 py-0.5 text-[11px] font-semibold text-primary transition-colors hover:bg-sky-100 hover:text-primary-dark"
                      >
                        <Shield className="h-3 w-3" aria-hidden="true" />
                        Chỉnh sửa {role.code}
                      </button>
                    )}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {PERMISSION_GROUPS.map((group) => (
              <Fragment key={group.module}>
                <tr className="bg-canvas/70">
                  <td
                    colSpan={roles.length + 1}
                    className="px-4 py-2 text-xs font-bold uppercase tracking-wide text-navy"
                  >
                    {group.module} ({group.permissions.length})
                  </td>
                </tr>
                {group.permissions.map((perm) => (
                  <tr key={perm.code} className="hover:bg-canvas/50">
                    <td className="px-4 py-2.5">
                      <p className="font-medium text-navy">{perm.name}</p>
                      <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
                        <span className="rounded border border-border bg-canvas px-1.5 py-0.5 font-mono text-[11px] text-ink">
                          {perm.code}
                        </span>
                        <span className="text-[11px] text-sub">{perm.module}</span>
                      </div>
                    </td>
                    {roles.map((role) => {
                      const hasPerm = role.permissions.some((item) => item.code === perm.code);
                      return (
                        <td
                          key={`${role.id}-${perm.code}`}
                          className="border-l border-border px-3 py-2.5 text-center"
                        >
                          {hasPerm ? (
                            <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold text-emerald-700">
                              <Check className="h-3.5 w-3.5" aria-hidden="true" />
                              Đã cấp
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-sub">
                              <Minus className="h-3.5 w-3.5" aria-hidden="true" />
                              Chưa cấp
                            </span>
                          )}
                          <span className="sr-only">
                            {role.code}: {hasPerm ? 'Đã cấp' : 'Chưa cấp'}
                          </span>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
