import { ChevronLeft, ChevronRight } from 'lucide-react';

import { STAFF_STATUS_LABELS } from '../data/staff-account-mock-data';
import type { StaffAccountItem, StaffAccountStatus } from '../types/staff-account.types';

interface StaffAccountTableProps {
  items: StaffAccountItem[];
  total: number;
  page: number;
  totalPages: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}

const STATUS_STYLES: Record<StaffAccountStatus, string> = {
  ACTIVE: 'bg-emerald-50 text-emerald-700',
  LOCKED: 'bg-amber-50 text-amber-700',
  DISABLED: 'bg-slate-100 text-sub',
};

const COLUMNS = [
  'MÃ NV',
  'NHÂN SỰ / EMAIL',
  'VAI TRÒ',
  'KHOA CÔNG TÁC',
  'CƠ SỞ',
  'TRẠNG THÁI',
  'THAO TÁC',
];

export function StaffAccountTable({
  items,
  total,
  page,
  totalPages,
  pageSize,
  onPageChange,
}: StaffAccountTableProps) {
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
                  Không tìm thấy tài khoản nhân sự phù hợp.
                </td>
              </tr>
            ) : (
              items.map((item) => (
                <tr key={item.id} className="hover:bg-canvas">
                  <td className="px-4 py-3 font-semibold text-navy">{item.code}</td>
                  <td className="px-4 py-3">
                    <p className="font-medium text-navy">{item.fullName}</p>
                    <p className="text-xs text-sub">{item.email}</p>
                  </td>
                  <td className="px-4 py-3 text-ink">{item.role}</td>
                  <td className="px-4 py-3 text-ink">{item.department}</td>
                  <td className="px-4 py-3 text-ink">{item.facility}</td>
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex rounded-full px-2.5 py-1 text-xs font-semibold ${STATUS_STYLES[item.status]}`}
                    >
                      {STAFF_STATUS_LABELS[item.status]}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <button
                      type="button"
                      aria-label={`Xem chi tiết ${item.fullName}`}
                      className="text-sm font-semibold text-primary hover:text-primary-dark"
                    >
                      Xem
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <div className="flex items-center justify-between border-t border-border px-4 py-3 text-sm text-sub">
        <span>
          Hiển thị {from}–{to} / {total} tài khoản
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
