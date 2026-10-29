import { ChevronLeft, ChevronRight, Eye } from 'lucide-react';

import { ROOM_STATUS_LABELS } from '../types/room.types';
import type { RoomItem, RoomStatus } from '../types/room.types';

interface RoomTableProps {
  rooms: RoomItem[];
  totalCount: number;
  currentPage: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

const STATUS_STYLES: Record<RoomStatus, string> = {
  ACTIVE: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  SUSPENDED: 'border-amber-200 bg-amber-50 text-amber-700',
  MAINTENANCE: 'border-rose-200 bg-rose-50 text-rose-700',
};

export function RoomTable({
  rooms,
  totalCount,
  currentPage,
  pageSize,
  totalPages,
  onPageChange,
}: RoomTableProps) {
  const from = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const to = Math.min(currentPage * pageSize, totalCount);

  return (
    <section
      aria-label="Bảng danh mục buồng khám"
      className="overflow-hidden rounded-xl border border-border bg-white shadow-sm"
    >
      <table className="w-full text-left text-xs">
        <thead className="bg-canvas font-mono text-[10px] font-bold uppercase tracking-wider text-sub">
          <tr className="h-10">
            <th className="px-4">MÃ PHÒNG</th>
            <th className="px-2">TÊN BUỒNG KHÁM</th>
            <th className="px-2">KHOA PHÒNG</th>
            <th className="px-2">VỊ TRÍ / TẦNG</th>
            <th className="px-2">TRẠNG THÁI KHẢ DỤNG</th>
            <th className="px-4 text-right">THAO TÁC</th>
          </tr>
        </thead>
        <tbody>
          {rooms.length === 0 ? (
            <tr>
              <td colSpan={6} className="px-4 py-10 text-center text-sub">
                Không tìm thấy buồng khám phù hợp.
              </td>
            </tr>
          ) : (
            rooms.map((room) => (
              <tr key={room.id} className="h-12 border-t border-border hover:bg-slate-50">
                <td className="px-4 font-mono font-bold text-primary">{room.code}</td>
                <td className="px-2 font-semibold text-navy">{room.name}</td>
                <td className="px-2 text-ink">{room.department}</td>
                <td className="px-2 text-sub">{room.location}</td>
                <td className="px-2">
                  <span
                    className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${STATUS_STYLES[room.status]}`}
                  >
                    {ROOM_STATUS_LABELS[room.status]}
                  </span>
                </td>
                <td className="px-4 text-right">
                  <button
                    type="button"
                    aria-label={`Xem chi tiết ${room.code}`}
                    className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-primary"
                  >
                    <Eye className="h-4 w-4" aria-hidden="true" />
                  </button>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>

      <div className="flex items-center justify-between border-t border-border px-4 py-3 text-xs text-sub">
        <span>
          Hiển thị {from}–{to} / {totalCount} buồng khám
        </span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            aria-label="Trang trước"
            disabled={currentPage <= 1}
            onClick={() => onPageChange(currentPage - 1)}
            className="rounded-md border border-border p-1.5 hover:bg-slate-50 disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          </button>
          <span className="font-semibold text-ink">
            Trang {currentPage} / {totalPages}
          </span>
          <button
            type="button"
            aria-label="Trang sau"
            disabled={currentPage >= totalPages}
            onClick={() => onPageChange(currentPage + 1)}
            className="rounded-md border border-border p-1.5 hover:bg-slate-50 disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </div>
    </section>
  );
}
