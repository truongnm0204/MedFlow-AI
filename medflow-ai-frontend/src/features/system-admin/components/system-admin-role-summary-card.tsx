import { ShieldCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/ui/card';

import type { SystemAdminRoleSummaryDto } from '../types/system-admin.types';

interface SystemAdminRoleSummaryCardProps {
  roles: SystemAdminRoleSummaryDto[];
}

export function SystemAdminRoleSummaryCard({ roles }: SystemAdminRoleSummaryCardProps) {
  return (
    <Card>
      <CardHeader className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <ShieldCheck aria-hidden="true" className="h-4 w-4 text-primary" />
          <div>
            <h2 className="text-sm font-bold text-ink">
              Tổng quan Vai trò &amp; Phân quyền (RBAC)
            </h2>
            <p className="text-[11px] text-sub">
              Thống kê người dùng hoạt động và quyền hạn theo từng vai trò
            </p>
          </div>
        </div>
        <Badge tone="sky">{roles.length} vai trò</Badge>
      </CardHeader>

      <CardContent className="p-0">
        <ul className="divide-y divide-slate-100">
          {roles.map((role) => (
            <li key={role.roleId} className="space-y-1.5 px-6 py-3">
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="rounded border border-sky-200 bg-sky-50 px-1.5 py-0.5 font-mono text-[10px] font-bold text-primary">
                    {role.code}
                  </span>
                  <span className="text-xs font-bold text-ink">{role.name}</span>
                </div>
                <Badge tone={role.activeUserCount > 0 ? 'emerald' : 'slate'}>
                  {role.activeUserCount}/{role.assignedUserCount} TK hoạt động
                </Badge>
              </div>

              <div className="flex flex-wrap items-center gap-1 pt-0.5">
                <span className="text-[11px] font-medium text-sub">
                  {role.permissionCount} quyền:
                </span>
                {role.permissions.length > 0 ? (
                  role.permissions.map((perm) => (
                    <span
                      key={perm}
                      className="rounded border border-border bg-canvas px-1.5 py-0.5 font-mono text-[10px] font-medium text-sub"
                    >
                      {perm}
                    </span>
                  ))
                ) : (
                  <span className="text-[10px] italic text-sub">Chưa gán quyền</span>
                )}
              </div>
            </li>
          ))}
        </ul>

        <div className="border-t border-border bg-canvas px-6 py-2.5 text-[11px] text-sub">
          Giới hạn phạm vi RBAC: Chỉ hiển thị các lối vào chức năng Quản trị Hệ thống (UC57–UC60)
          theo quyền được cấp. Các thao tác khám lâm sàng, kê đơn và cấp phát thuốc bị ẩn hoàn toàn.
        </div>
      </CardContent>
    </Card>
  );
}
