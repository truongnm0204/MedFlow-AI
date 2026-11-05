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
      <div className="p-6 sm:p-7 text-left">
        {/* Header với icon cảnh báo và tiêu đề */}
        <div className="flex items-center gap-3.5">
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-slate-200 bg-slate-50 text-base font-bold text-slate-800"
            aria-hidden="true"
          >
            !
          </span>
          <div>
            <h2 id="deactivate-room-title" className="text-xl font-bold text-ink">
              Ngừng hoạt động buồng khám?
            </h2>
          </div>
        </div>

        {/* Thông tin định danh buồng khám */}
        <p className="mt-4 text-sm font-semibold text-slate-800">
          {room.roomName} · <span className="font-mono text-slate-700">{room.roomCode}</span>
        </p>

        {/* Mô tả giải thích tác động của hành động */}
        <p id="deactivate-room-desc" className="mt-3 text-xs text-sub leading-relaxed">
          Buồng khám sẽ không nhận lượt khám hoặc phân công ca khám mới. Lịch sử khám, chỉ định cận
          lâm sàng và hồ sơ bệnh án vẫn được lưu.
        </p>

        {/* Khung cảnh báo điều kiện tiên quyết trước khi xác nhận */}
        <div
          className={`mt-4 rounded-xl border p-4 text-xs ${
            isBlocked
              ? 'border-rose-200 bg-rose-50/70 text-rose-900'
              : 'border-amber-200/90 bg-[#FFFDF7] text-amber-900'
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
        <p className="mt-3 text-xs text-sub leading-normal">
          Xóa cứng không áp dụng: hồ sơ và mọi liên kết lịch sử tiếp đón được bảo toàn để kiểm tra.
        </p>

        {/* Nút thao tác chân modal */}
        <div className="mt-6 flex items-center justify-end gap-3 pt-2">
          <button
            type="button"
            onClick={onClose}
            disabled={loading}
            className="rounded-xl border border-border bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            Giữ buồng khám
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={isBlocked || loading}
            aria-disabled={isBlocked || loading}
            className="rounded-xl bg-[#FEF08A] px-5 py-2.5 text-xs font-bold text-amber-950 shadow-2xs transition-colors hover:bg-amber-300 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {loading ? 'Đang xử lý...' : 'Ngừng hoạt động'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
