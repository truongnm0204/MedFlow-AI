import { ChevronLeft, ChevronRight } from 'lucide-react';

import { ROLE_SCOPE_LABELS, ROLE_STATUS_LABELS } from '../data/role-mock-data';
import type { RoleItem, RoleStatus } from '../types/role.types';

interface RoleTableProps {
  items: RoleItem[];
  total: number;
  page: number;
  totalPages: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onSelectRole: (role: RoleItem) => void;
  onAssignPermissions?: (role: RoleItem) => void;
}

const STATUS_STYLES: Record<RoleStatus, string> = {
  ACTIVE: 'bg-emerald-50 text-emerald-700',
  INACTIVE: 'bg-slate-100 text-sub',
};

const COLUMNS = [
  'MÃ VAI TRÒ',
  'TÊN VAI TRÒ / MÔ TẢ',
  'NHÓM PHÂN HỆ',
  'QUYỀN HẠN',
  'TÀI KHOẢN',
  'TRẠNG THÁI',
  'THAO TÁC',
];

export function RoleTable({
  items,
  total,
  page,
  totalPages,
  pageSize,
  onPageChange,
  onSelectRole,
  onAssignPermissions,
}: RoleTableProps) {
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-white shadow-sm">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-canvas text-xs font-semibold tracking-wide text-sub">
            <tr>
              {COLUMNS.map((column) => (
                <th key={column} scope="col" className="px-4 py-3">
                  {column}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {items.length === 0 ? (
              <tr>
                <td colSpan={COLUMNS.length} className="px-4 py-10 text-center text-sub">
                  Không tìm thấy vai trò hệ thống phù hợp.
                </td>
              </tr>
            ) : (
              items.map((item) => (
                <tr key={item.id} className="hover:bg-canvas">
                  <td className="px-4 py-3">
                    <div className="flex flex-col items-start gap-1">
                      <span className="font-mono font-semibold text-navy">{item.code}</span>
                      <span
                        className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-medium ${
                          item.isSystemRole ? 'bg-sky-50 text-primary' : 'bg-slate-100 text-sub'
                        }`}
                      >
                        {item.isSystemRole ? 'Hệ thống' : 'Tùy chỉnh'}
                      </span>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <p className="font-semibold text-navy">{item.name}</p>
                    <p className="text-xs text-sub">{item.description}</p>
                  </td>
                  <td className="px-4 py-3 text-ink">{ROLE_SCOPE_LABELS[item.scope]}</td>
                  <td className="px-4 py-3">
                    <div className="flex flex-col gap-1">
                      <span className="text-xs font-semibold text-navy">
                        {item.permissions.length} quyền
                      </span>
                      <div className="flex flex-wrap items-center gap-1">
                        {item.permissions.slice(0, 2).map((perm) => (
                          <span
                            key={perm.code}
                            className="rounded border border-border bg-canvas px-1.5 py-0.5 font-mono text-[11px] text-ink"
                          >
                            {perm.code}
                          </span>
                        ))}
                        {item.permissions.length > 2 && (
                          <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-sub">
                            +{item.permissions.length - 2}
                          </span>
                        )}
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-ink">{item.assignedUsersCount} tài khoản</td>
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex rounded-full px-2.5 py-1 text-xs font-semibold ${STATUS_STYLES[item.status]}`}
                    >
                      {ROLE_STATUS_LABELS[item.status]}
                      <span className="sr-only">{item.status}</span>
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        onClick={() => onSelectRole(item)}
                        aria-label={`Xem chi tiết ${item.name}`}
                        className="text-sm font-semibold text-primary hover:text-primary-dark"
                      >
                        Xem quyền
                      </button>
                      {onAssignPermissions && (
                        <button
                          type="button"
                          onClick={() => onAssignPermissions(item)}
                          aria-label={`Phân quyền ${item.name}`}
                          className="rounded-lg border border-sky-200 bg-sky-50 px-2.5 py-1 text-xs font-semibold text-primary transition-colors hover:bg-sky-100 hover:text-primary-dark"
                        >
                          Phân quyền
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <div className="flex items-center justify-between border-t border-border px-4 py-3 text-sm text-sub">
        <span>
          Hiển thị {from}–{to} / {total} vai trò
        </span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            aria-label="Trang trước"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            className="rounded-md border border-border p-1.5 text-navy hover:bg-canvas disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          </button>
          <span>
            Trang {page} / {totalPages}
          </span>
          <button
            type="button"
            aria-label="Trang sau"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            className="rounded-md border border-border p-1.5 text-navy hover:bg-canvas disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </div>
    </div>
  );
}
