import {
  ArrowRight,
  CheckCircle2,
  Clock,
  FileText,
  MapPin,
  Printer,
  ShieldCheck,
  User,
  Users,
  X,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { CheckInPriority, CheckInSuccessResult } from '../types/checkin.types';

const PRIORITY_BADGE_CONFIG: Record<
  CheckInPriority,
  { label: string; tone: 'emerald' | 'amber' | 'rose' | 'sky' }
> = {
  NORMAL: { label: 'Khám thường', tone: 'sky' },
  URGENT: { label: 'Cấp cứu / Khẩn cấp', tone: 'rose' },
  ELDERLY: { label: 'Ưu tiên người cao tuổi', tone: 'amber' },
  PREGNANT: { label: 'Ưu tiên thai phụ / trẻ nhỏ', tone: 'emerald' },
};

interface CheckInResultContentProps {
  result: CheckInSuccessResult;
  onClose: () => void;
  onPrint?: () => void;
}

export function CheckInResultContent({
  result,
  onClose,
  onPrint = () => window.print(),
}: CheckInResultContentProps) {
  const priorityConfig = result.priority
    ? PRIORITY_BADGE_CONFIG[result.priority]
    : PRIORITY_BADGE_CONFIG.NORMAL;

  const instructions = result.nextStepInstructions ?? [
    `Di chuyển đến trước ${result.roomName} (${result.departmentName ?? 'Khu khám ngoại trú'} - Tầng 1).`,
    'Theo dõi màn hình điện tử và lắng nghe loa gọi số thứ tự tiếp nhận.',
    'Chuẩn bị sẵn thẻ CCCD/BHYT bản gốc trước khi vào phòng khám.',
  ];

  return (
    <div
      data-testid="checkin-success-view"
      className="py-2 text-center space-y-6 animate-in fade-in zoom-in-95 duration-150"
    >
      {/* Success Badge */}
      <div className="w-16 h-16 rounded-3xl bg-emerald-100 text-emerald-600 border border-emerald-200 flex items-center justify-center mx-auto shadow-xs">
        <CheckCircle2 className="w-9 h-9" aria-hidden="true" />
      </div>

      <div className="space-y-1">
        <div className="flex items-center justify-center gap-2">
          <span className="text-xs font-bold text-emerald-700 uppercase tracking-wider">
            Check-in Thành Công
          </span>
          <Badge tone={priorityConfig.tone}>{priorityConfig.label}</Badge>
        </div>
        <h3 className="text-2xl font-black text-slate-900">
          Số Thứ Tự: <span className="font-mono text-primary">{result.ticketNo}</span>
        </h3>
        <p className="text-xs text-slate-500">
          Lượt khám kích hoạt:{' '}
          <span className="font-mono font-bold text-slate-700">{result.visitId}</span>
        </p>
      </div>

      {/* Ticket & Room Information Card */}
      <div className="max-w-lg mx-auto p-5 rounded-2xl bg-canvas border border-border text-left text-xs space-y-3">
        <div className="flex justify-between items-center py-1.5 border-b border-slate-200/60">
          <span className="text-slate-500 flex items-center gap-1.5">
            <User className="w-3.5 h-3.5 text-slate-400" aria-hidden="true" />
            Bệnh nhân:
          </span>
          <div className="text-right">
            <span className="font-bold text-slate-900">{result.patientName}</span>
            {result.patientCode && (
              <span className="ml-2 font-mono text-[11px] text-slate-500">
                ({result.patientCode})
              </span>
            )}
          </div>
        </div>

        <div className="flex justify-between items-center py-1.5 border-b border-slate-200/60">
          <span className="text-slate-500 flex items-center gap-1.5">
            <MapPin className="w-3.5 h-3.5 text-slate-400" aria-hidden="true" />
            Phòng khám:
          </span>
          <span className="font-bold text-emerald-700 text-sm">
            {result.roomName} {result.departmentName ? `• ${result.departmentName}` : ''}
          </span>
        </div>

        <div className="flex justify-between items-center py-1.5 border-b border-slate-200/60">
          <span className="text-slate-500 flex items-center gap-1.5">
            <ShieldCheck className="w-3.5 h-3.5 text-slate-400" aria-hidden="true" />
            Bác sĩ khám:
          </span>
          <span className="font-bold text-slate-800">{result.doctorName}</span>
        </div>

        <div className="flex justify-between items-center py-1.5">
          <span className="text-slate-500 flex items-center gap-1.5">
            <Clock className="w-3.5 h-3.5 text-slate-400" aria-hidden="true" />
            Thời gian check-in:
          </span>
          <span className="font-mono text-slate-700">{result.checkedInAt}</span>
        </div>
      </div>

      {/* Queue & Estimated Waiting Section */}
      <div className="max-w-lg mx-auto grid grid-cols-2 gap-3 text-left">
        <div className="p-3.5 rounded-2xl bg-sky-50/60 border border-sky-100 flex items-center gap-3">
          <div className="w-9 h-9 rounded-xl bg-sky-100 text-sky-700 flex items-center justify-center shrink-0">
            <Users className="w-4 h-4" aria-hidden="true" />
          </div>
          <div>
            <div className="text-[11px] text-sky-800 font-semibold">Đang chờ trước</div>
            <div className="text-sm font-black text-sky-950 font-mono">
              {result.waitingCountBefore ?? 3} người
            </div>
          </div>
        </div>

        <div className="p-3.5 rounded-2xl bg-amber-50/60 border border-amber-100 flex items-center gap-3">
          <div className="w-9 h-9 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center shrink-0">
            <Clock className="w-4 h-4" aria-hidden="true" />
          </div>
          <div>
            <div className="text-[11px] text-amber-800 font-semibold">Thời gian dự kiến</div>
            <div className="text-sm font-black text-amber-950 font-mono">
              ~{result.estimatedWaitMinutes ?? 15} phút
            </div>
          </div>
        </div>
      </div>

      {/* Next Step Instructions for Patient */}
      <div className="max-w-lg mx-auto p-4 rounded-2xl bg-slate-50 border border-slate-200/80 text-left space-y-2.5">
        <div className="text-xs font-bold text-slate-800 flex items-center gap-1.5">
          <FileText className="w-3.5 h-3.5 text-slate-500" aria-hidden="true" />
          <span>Hướng dẫn tiếp theo cho người bệnh</span>
        </div>
        <ol className="space-y-1.5 text-xs text-slate-600 pl-1">
          {instructions.map((step, idx) => (
            <li key={idx} className="flex items-start gap-2">
              <span className="w-4 h-4 rounded-full bg-slate-200 text-slate-700 flex items-center justify-center font-bold text-[10px] shrink-0 mt-0.5">
                {idx + 1}
              </span>
              <span>{step}</span>
            </li>
          ))}
        </ol>
      </div>

      {/* Action Buttons */}
      <div className="flex items-center justify-center gap-3 pt-2">
        <Button variant="secondary" size="sm" onClick={onPrint} className="gap-2">
          <Printer className="w-4 h-4" aria-hidden="true" />
          In phiếu tiếp nhận (STT)
        </Button>
        <Button variant="primary" size="sm" onClick={onClose} className="gap-2">
          <span>Hoàn tất & Đóng</span>
          <ArrowRight className="w-4 h-4" aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}

interface CheckInResultModalProps {
  isOpen: boolean;
  onClose: () => void;
  result: CheckInSuccessResult | null;
  onPrint?: () => void;
}

export function CheckInResultModal({ isOpen, onClose, result, onPrint }: CheckInResultModalProps) {
  if (!isOpen || !result) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="checkin-result-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Modal Dialog */}
      <div className="relative w-full max-w-2xl bg-white rounded-3xl shadow-2xl border border-border overflow-hidden z-10 flex flex-col max-h-[90vh] animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="bg-gradient-to-r from-emerald-50 via-white to-emerald-50/50 p-6 border-b border-border flex items-start justify-between">
          <div className="flex items-center gap-3.5">
            <div className="w-12 h-12 rounded-2xl bg-emerald-100 text-emerald-700 border border-emerald-200 flex items-center justify-center shadow-xs">
              <CheckCircle2 className="w-6 h-6" aria-hidden="true" />
            </div>
            <div>
              <div className="text-xs font-bold text-emerald-700 tracking-wide uppercase">
                Tiếp Nhận Ngoại Trú • Quầy Lễ Tân
              </div>
              <h2 id="checkin-result-title" className="text-lg font-bold text-ink mt-0.5">
                Kết Quả Tiếp Nhận & Phiếu Khám
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng kết quả check-in"
            className="p-2 rounded-xl text-sub hover:text-ink hover:bg-slate-100 transition-colors"
          >
            <X className="w-5 h-5" aria-hidden="true" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-6 overflow-y-auto">
          <CheckInResultContent result={result} onClose={onClose} onPrint={onPrint} />
        </div>
      </div>
    </div>
  );
}
