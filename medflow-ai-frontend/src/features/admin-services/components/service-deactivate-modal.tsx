import { useState } from 'react';
import { AlertTriangle } from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import type { ExaminationService } from '../types/examination-service.types';

interface ServiceDeactivateModalProps {
  service: ExaminationService | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (serviceId: string) => Promise<void> | void;
  isSubmitting?: boolean;
}

/**
 * SCR-ADM-ESV-05 — Modal xác nhận ngừng hoạt động dịch vụ khám.
 */
export function ServiceDeactivateModal({
  service,
  open,
  onClose,
  onConfirm,
  isSubmitting = false,
}: ServiceDeactivateModalProps) {
  const [internalSubmitting, setInternalSubmitting] = useState(false);

  if (!open || !service) return null;

  const isBlocked = Boolean(service.hasActiveBookings);
  const loading = isSubmitting || internalSubmitting;

  const handleConfirm = async () => {
    if (isBlocked || loading) return;
    try {
      setInternalSubmitting(true);
      await onConfirm(service.id);
    } finally {
      setInternalSubmitting(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-[560px]" hideCloseButton>
      <div className="p-6 sm:p-7">
        {/* Header với icon cảnh báo và tiêu đề */}
        <div className="flex items-center gap-3.5">
          <span
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full border border-slate-200 bg-slate-50 text-base font-bold text-slate-800"
            aria-hidden="true"
          >
            <AlertTriangle className="h-5 w-5 text-amber-600" aria-hidden="true" />
          </span>
          <h2 id="deactivate-service-title" className="text-xl font-bold text-ink">
            Ngừng hoạt động dịch vụ khám?
          </h2>
        </div>

        {/* Thông tin định danh dịch vụ: Tên · Mã dịch vụ */}
        <p className="mt-4 text-sm font-semibold text-slate-800">
          {service.name} · <span className="font-mono text-slate-700">{service.code}</span>
        </p>
        <p className="mt-1 text-xs text-sub">
          {service.department} • {service.category}
        </p>

        {/* Mô tả giải thích tác động của hành động */}
        <p id="deactivate-service-desc" className="mt-3 text-xs text-sub leading-relaxed">
          Dịch vụ sẽ không thể đặt lịch khám mới trên Cổng thông tin người bệnh hoặc tiếp nhận tại
          quầy. Lịch sử khám bệnh, hồ sơ bệnh án và lịch sử giá vẫn được lưu trữ toàn vẹn.
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
              ? `Không thể ngừng hoạt động khi còn ${service.activeBookingsCount ?? 'lượt'} lịch hẹn khám hoặc ca tiếp nhận đang chờ xử lý.`
              : 'Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc lịch tương lai chưa được giải quyết.'}
          </p>
        </div>

        {/* Ghi chú pháp lý / bảo toàn dữ liệu lịch sử */}
        <p className="mt-3 text-xs text-sub leading-normal">
          Xóa cứng không áp dụng: hồ sơ danh mục và mọi liên kết lịch sử được bảo toàn để kiểm toán
          và đối soát viện phí.
        </p>

        {/* Nút thao tác chân modal */}
        <div className="mt-6 flex items-center justify-end gap-3 pt-2">
          <button
            type="button"
            onClick={onClose}
            disabled={loading}
            className="rounded-xl border border-border bg-white px-5 py-2.5 text-xs font-semibold text-slate-700 transition-colors hover:bg-slate-50 disabled:opacity-50"
          >
            Giữ dịch vụ
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
