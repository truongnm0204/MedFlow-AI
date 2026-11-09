import { CheckCircle2, ChevronRight, Printer, Sparkles, X } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import type { CreatedWalkInVisitResult } from '../types/walk-in-visit.types';

interface WalkInSuccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  result: CreatedWalkInVisitResult | null;
  onGoToRouting?: () => void;
}

export function WalkInSuccessModal({
  isOpen,
  onClose,
  result,
  onGoToRouting,
}: WalkInSuccessModalProps) {
  if (!isOpen || !result) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="walkin-success-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Modal Dialog */}
      <div className="relative w-full max-w-lg bg-white rounded-3xl shadow-2xl border border-border overflow-hidden z-10 flex flex-col animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="bg-gradient-to-r from-emerald-50 via-white to-emerald-50/50 p-6 border-b border-border flex items-start justify-between">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-2xl bg-emerald-100 text-emerald-700 border border-emerald-200 flex items-center justify-center shadow-xs">
              <CheckCircle2 className="w-6 h-6" aria-hidden="true" />
            </div>
            <div>
              <span className="text-[11px] font-bold text-emerald-700 uppercase tracking-wider">
                Tiếp Nhận Thành Công
              </span>
              <h2 id="walkin-success-title" className="text-lg font-bold text-slate-900 mt-0.5">
                Đã Tạo Lượt Khám Vãng Lai
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng thông báo"
            className="p-2 rounded-xl text-sub hover:text-ink hover:bg-slate-100 transition-colors"
          >
            <X className="w-5 h-5" aria-hidden="true" />
          </button>
        </div>

        {/* Content body */}
        <div className="p-6 space-y-5 text-center">
          <div className="space-y-1">
            <div className="text-xs text-slate-500 font-medium">Số thứ tự tiếp đón</div>
            <div className="text-3xl font-black text-primary font-mono tracking-tight">
              {result.ticketNo}
            </div>
            <div className="text-xs text-slate-600 font-mono">
              Mã lượt khám: <strong className="text-slate-800">{result.visitCode}</strong>
            </div>
          </div>

          {/* Ticket summary card */}
          <div className="p-4.5 rounded-2xl bg-canvas border border-border text-left text-xs space-y-2.5">
            <div className="flex justify-between py-1 border-b border-slate-200/60">
              <span className="text-slate-500">Bệnh nhân:</span>
              <span className="font-bold text-slate-900">{result.patientName}</span>
            </div>
            <div className="flex justify-between py-1 border-b border-slate-200/60">
              <span className="text-slate-500">Khoa tiếp nhận:</span>
              <span className="font-bold text-sky-700">{result.departmentName}</span>
            </div>
            <div className="flex justify-between py-1 border-b border-slate-200/60">
              <span className="text-slate-500">Dịch vụ:</span>
              <span className="font-medium text-slate-800">{result.serviceName}</span>
            </div>
            <div className="flex justify-between py-1 border-b border-slate-200/60">
              <span className="text-slate-500">Chi phí:</span>
              <span className="font-mono font-bold text-slate-900">{result.priceFormatted}</span>
            </div>
            <div className="flex justify-between py-1">
              <span className="text-slate-500">Hình thức thanh toán:</span>
              <span className="font-medium text-emerald-700">
                {result.paymentMethod === 'VIETQR'
                  ? 'Quét mã VietQR tại quầy'
                  : 'Quầy thu ngân Tầng 1'}
              </span>
            </div>
          </div>

          <div className="p-3 rounded-xl bg-sky-50 border border-sky-100 text-xs text-sky-800 flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-sky-600 shrink-0" aria-hidden="true" />
            <span>Lượt khám đã sẵn sàng được phân buồng bác sĩ theo tải lượng thực tế.</span>
          </div>
        </div>

        {/* Footer */}
        <div className="p-5 border-t border-border bg-slate-50 flex items-center justify-between gap-3">
          <Button variant="secondary" size="sm" onClick={() => window.print()} className="gap-1.5">
            <Printer className="w-4 h-4" aria-hidden="true" />
            <span>In phiếu tiếp nhận</span>
          </Button>

          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" onClick={onClose}>
              Đóng
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={onGoToRouting}
              className="gap-1.5 shadow-xs"
            >
              <span>Phân buồng khám</span>
              <ChevronRight className="w-4 h-4" aria-hidden="true" />
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
