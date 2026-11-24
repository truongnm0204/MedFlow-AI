import { AlertTriangle } from 'lucide-react';
import { useState } from 'react';

import { Modal } from '@/shared/ui/modal';

import type { RoomItem } from '../types/room.types';

interface RoomDeactivateModalProps {
  room: RoomItem | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (roomId: string) => Promise<void> | void;
  isSubmitting?: boolean;
}

/**
 * SCR-ADM-ROOM-05 — Modal xác nhận ngừng hoạt động buồng khám (M2-179).
 */
export function RoomDeactivateModal({
  room,
  open,
  onClose,
  onConfirm,
  isSubmitting = false,
}: RoomDeactivateModalProps) {
  const [internalSubmitting, setInternalSubmitting] = useState(false);

  if (!open || !room) return null;

  const assignedDoctorsCount = room.assignedDoctorsCount ?? 0;
  const activeVisitsCount = room.activeVisitsCount ?? 0;
  const activeSlotsCount = room.activeSlotsCount ?? 0;

  const isBlocked =
    Boolean(room.hasActiveDependencies) ||
    assignedDoctorsCount > 0 ||
    activeVisitsCount > 0 ||
    activeSlotsCount > 0;

  const loading = isSubmitting || internalSubmitting;

  const handleConfirm = async () => {
    if (isBlocked || loading) return;
    try {
      setInternalSubmitting(true);
      await onConfirm(room.id);
    } finally {
      setInternalSubmitting(false);
    }
  };

  const getDependencyText = () => {
    const parts: string[] = [];
    if (assignedDoctorsCount > 0) parts.push(`${assignedDoctorsCount} bác sĩ đang trực`);
    if (activeVisitsCount > 0) parts.push(`${activeVisitsCount} lượt khám đang xử lý`);
    if (activeSlotsCount > 0) parts.push(`${activeSlotsCount} ca khám đang mở`);
    return parts.length > 0 ? parts.join(', ') : 'lượt khám hoặc ca khám chưa giải quyết';
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-[560px]">
      <div className="p-6 text-left sm:p-7">
        {/* Header với icon cảnh báo và tiêu đề */}
        <div className="flex items-center gap-3.5">
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-border bg-canvas text-amber-600"
            aria-hidden="true"
          >
            <AlertTriangle className="h-5 w-5" aria-hidden="true" />
          </span>
          <div>
            <h2 id="deactivate-room-title" className="text-xl font-bold text-ink">
              Ngừng hoạt động buồng khám?
            </h2>
          </div>
        </div>

        {/* Thông tin định danh buồng khám */}
        <p className="mt-4 text-sm font-semibold text-ink">
          {room.roomName} · <span className="font-mono text-sub">{room.roomCode}</span>
        </p>

        {/* Mô tả giải thích tác động của hành động */}
        <p id="deactivate-room-desc" className="mt-3 text-xs leading-relaxed text-sub">
          Buồng khám sẽ không nhận lượt khám hoặc phân công ca khám mới. Lịch sử khám, chỉ định cận
          lâm sàng và hồ sơ bệnh án vẫn được lưu.
        </p>

        {/* Khung cảnh báo điều kiện tiên quyết trước khi xác nhận */}
        <div
          className={`mt-4 rounded-xl border p-4 text-xs ${
            isBlocked
              ? 'border-rose-200 bg-rose-50/70 text-rose-900'
              : 'border-amber-200 bg-amber-50/70 text-amber-900'
          }`}
        >
          <p className={`font-bold ${isBlocked ? 'text-rose-900' : 'text-amber-900'}`}>
            Trước khi xác nhận
          </p>
          <p
            className={`mt-1.5 leading-relaxed ${
              isBlocked ? 'text-rose-800' : 'text-amber-800/90'
            }`}
          >
            {isBlocked
              ? `Không thể ngừng hoạt động khi còn ${getDependencyText()}. Vui lòng chuyển giao hoặc đóng ca trước khi thực hiện.`
              : 'Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc ca khám chưa được giải quyết.'}
          </p>
        </div>

        {/* Ghi chú pháp lý / bảo toàn dữ liệu lịch sử */}
        <p className="mt-3 text-xs leading-normal text-sub">
          Xóa cứng không áp dụng: hồ sơ và mọi liên kết lịch sử tiếp đón được bảo toàn để kiểm tra.
        </p>

        {/* Nút thao tác chân modal */}
        <div className="mt-6 flex items-center justify-end gap-3 pt-2">
          <button
            type="button"
            onClick={onClose}
            disabled={loading}
            className="rounded-xl border border-border bg-white px-5 py-2.5 text-xs font-semibold text-ink transition-colors hover:bg-canvas focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none disabled:opacity-60"
          >
            Giữ buồng khám
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={isBlocked || loading}
            aria-disabled={isBlocked || loading}
            className="rounded-xl bg-amber-200 px-5 py-2.5 text-xs font-bold text-amber-950 shadow-2xs transition-colors hover:bg-amber-300 focus-visible:ring-2 focus-visible:ring-amber-500 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-60"
          >
            {loading ? 'Đang xử lý...' : 'Ngừng hoạt động'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
