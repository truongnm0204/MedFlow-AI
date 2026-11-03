import { useState } from 'react';
import { ChevronLeft, ChevronRight, Eye } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { RoomItem, RoomStatus } from '../types/room.types';

interface RoomTableProps {
  rooms: RoomItem[];
  onViewRoom?: (room: RoomItem) => void;
}

const STATUS_CONFIG: Record<RoomStatus, { label: string; tone: 'emerald' | 'amber' | 'slate' }> = {
  ACTIVE: { label: 'Đang hoạt động', tone: 'emerald' },
  MAINTENANCE: { label: 'Bảo trì', tone: 'amber' },
  INACTIVE: { label: 'Tạm ngừng', tone: 'slate' },
};

const ITEMS_PER_PAGE = 8;

export function RoomTable({ rooms, onViewRoom }: RoomTableProps) {
  const [currentPage, setCurrentPage] = useState(1);

  const totalPages = Math.ceil(rooms.length / ITEMS_PER_PAGE) || 1;
  const startIndex = (currentPage - 1) * ITEMS_PER_PAGE;
  const paginatedRooms = rooms.slice(startIndex, startIndex + ITEMS_PER_PAGE);

  return (
    <div className="rounded-2xl border border-border bg-white shadow-2xs overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs border-collapse">
          <thead>
            <tr className="border-b border-border bg-slate-50/80 text-[11px] font-bold uppercase tracking-wider text-sub">
              <th scope="col" className="px-5 py-3.5">
                MÃ PHÒNG
              </th>
              <th scope="col" className="px-5 py-3.5">
                TÊN BUỒNG KHÁM
              </th>
              <th scope="col" className="px-5 py-3.5">
                KHOA PHÒNG
              </th>
              <th scope="col" className="px-5 py-3.5">
                VỊ TRÍ / TẦNG
              </th>
              <th scope="col" className="px-5 py-3.5">
                TRẠNG THÁI KHẢ DỤNG
              </th>
              <th scope="col" className="px-5 py-3.5 text-right">
                THAO TÁC
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {paginatedRooms.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-5 py-8 text-center text-sub">
                  Không tìm thấy buồng khám nào phù hợp với bộ lọc.
                </td>
              </tr>
            ) : (
              paginatedRooms.map((room) => {
                const statusCfg = STATUS_CONFIG[room.status];
                return (
                  <tr key={room.id} className="hover:bg-slate-50/60 transition-colors">
                    {/* Mã phòng */}
                    <td className="px-5 py-4 font-mono font-bold text-sky-700 whitespace-nowrap">
                      {room.roomCode}
                    </td>

                    {/* Tên buồng khám */}
                    <td className="px-5 py-4 font-bold text-ink text-sm">{room.roomName}</td>

                    {/* Khoa phòng */}
                    <td className="px-5 py-4 text-slate-700 font-medium">{room.department}</td>

                    {/* Vị trí */}
                    <td className="px-5 py-4 text-sub">{room.location}</td>

                    {/* Trạng thái */}
                    <td className="px-5 py-4">
                      <Badge tone={statusCfg.tone}>{statusCfg.label}</Badge>
                    </td>

                    {/* Thao tác */}
                    <td className="px-5 py-4 text-right">
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => onViewRoom?.(room)}
                        className="gap-1.5"
                      >
                        <Eye className="w-3.5 h-3.5" aria-hidden="true" />
                        <span>Xem</span>
                      </Button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="p-4 border-t border-border bg-slate-50/60 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-xs text-sub">
        <div>
          Hiển thị{' '}
          <span className="font-bold text-ink">{rooms.length === 0 ? 0 : startIndex + 1}</span> -{' '}
          <span className="font-bold text-ink">
            {Math.min(startIndex + ITEMS_PER_PAGE, rooms.length)}
          </span>{' '}
          trong tổng số <span className="font-bold text-ink">{rooms.length}</span> Buồng khám
        </div>

        <div className="flex items-center gap-2 self-end sm:self-center">
          <button
            type="button"
            disabled={currentPage <= 1}
            onClick={() => setCurrentPage((p) => Math.max(p - 1, 1))}
            className="p-1.5 rounded-lg border border-border bg-white text-slate-600 hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            aria-label="Trang trước"
          >
            <ChevronLeft className="w-4 h-4" aria-hidden="true" />
          </button>
          <span className="font-medium text-ink px-2">
            Trang {currentPage} / {totalPages}
          </span>
          <button
            type="button"
            disabled={currentPage >= totalPages}
            onClick={() => setCurrentPage((p) => Math.min(p + 1, totalPages))}
            className="p-1.5 rounded-lg border border-border bg-white text-slate-600 hover:bg-slate-100 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            aria-label="Trang sau"
          >
            <ChevronRight className="w-4 h-4" aria-hidden="true" />
          </button>
        </div>
      </div>
    </div>
  );
}
