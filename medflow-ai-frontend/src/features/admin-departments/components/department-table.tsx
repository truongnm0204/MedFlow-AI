import { ChevronLeft, ChevronRight, Eye, Power } from 'lucide-react';

import type { DepartmentItem } from '../types/department.types';

interface DepartmentTableProps {
  departments: DepartmentItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onRequestDeactivate?: (department: DepartmentItem) => void;
  onToggleStatus?: (departmentId: string) => void;
}

export function DepartmentTable({
  departments,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onRequestDeactivate,
  onToggleStatus,
}: DepartmentTableProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <section
      aria-label="Bảng danh mục khoa phòng"
      className="overflow-hidden rounded-xl border border-border bg-white shadow-sm"
    >
      <table className="w-full text-left text-sm">
        <thead className="bg-canvas text-xs font-semibold tracking-wide text-sub">
          <tr>
            <th scope="col" className="px-4 py-3">
              MÃ KHOA
            </th>
            <th scope="col" className="px-4 py-3">
              TÊN KHOA
            </th>
            <th scope="col" className="px-4 py-3">
              CƠ SỞ
            </th>
            <th scope="col" className="px-4 py-3">
              TRẠNG THÁI
            </th>
            <th scope="col" className="px-4 py-3 text-right">
              THAO TÁC
            </th>
          </tr>
        </thead>
        <tbody>
          {departments.length === 0 ? (
            <tr>
              <td colSpan={5} className="px-4 py-8 text-center text-sub">
                Không tìm thấy khoa phòng phù hợp
              </td>
            </tr>
          ) : (
            departments.map((dep) => (
              <tr
                key={dep.id}
                className="border-t border-border hover:bg-canvas/50 transition-colors"
              >
                <td className="px-4 py-3 font-medium text-navy">{dep.code}</td>
                <td className="px-4 py-3">
                  <div className="font-medium text-ink">{dep.name}</div>
                  {dep.status === 'ACTIVE' && dep.hasActiveDependencies && (
                    <span className="text-[11px] text-amber-700">
                      {dep.activeRoomsCount ?? 0} buồng · {dep.activeDoctorsCount ?? 0} bác sĩ
                    </span>
                  )}
                </td>
                <td className="px-4 py-3 text-sub">{dep.facility}</td>
                <td className="px-4 py-3">
                  <span
                    className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold ${
                      dep.status === 'ACTIVE'
                        ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                        : 'bg-slate-100 text-slate-600 border border-slate-200'
                    }`}
                  >
                    <span
                      className={`h-1.5 w-1.5 rounded-full ${
                        dep.status === 'ACTIVE' ? 'bg-emerald-500' : 'bg-slate-400'
                      }`}
                      aria-hidden="true"
                    />
                    {dep.status === 'ACTIVE' ? 'Đang hoạt động' : 'Tạm ngừng'}
                  </span>
                </td>
                <td className="px-4 py-3 text-right">
                  <div className="flex items-center justify-end gap-1.5">
                    <button
                      type="button"
                      aria-label={`Xem chi tiết ${dep.name}`}
                      title={`Xem chi tiết ${dep.name}`}
                      className="rounded-md p-1.5 text-sub hover:bg-canvas hover:text-navy transition-colors"
                    >
                      <Eye className="h-4 w-4" aria-hidden="true" />
                    </button>
                    {dep.status === 'ACTIVE' ? (
                      <button
                        type="button"
                        aria-label={`Ngừng hoạt động ${dep.name}`}
                        title="Ngừng hoạt động khoa phòng"
                        onClick={() => onRequestDeactivate?.(dep)}
                        className="rounded-md p-1.5 text-sub hover:bg-amber-50 hover:text-amber-700 transition-colors"
                      >
                        <Power className="h-4 w-4" aria-hidden="true" />
                      </button>
                    ) : (
                      <button
                        type="button"
                        aria-label={`Kích hoạt lại ${dep.name}`}
                        title="Kích hoạt lại khoa phòng"
                        onClick={() => onToggleStatus?.(dep.id)}
                        className="rounded-md p-1.5 text-sub hover:bg-emerald-50 hover:text-emerald-700 transition-colors"
                      >
                        <Power className="h-4 w-4" aria-hidden="true" />
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
      <div className="flex items-center justify-between border-t border-border px-4 py-3 text-sm text-sub">
        <span>
          Hiển thị {from}-{to} / {totalCount} khoa
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
            Trang {page}/{totalPages}
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
    </section>
  );
}
