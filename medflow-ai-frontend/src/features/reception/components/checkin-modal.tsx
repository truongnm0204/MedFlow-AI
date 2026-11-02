import { useState } from 'react';
import {
  CheckCircle2,
  Printer,
  Sparkles,
  UserCheck,
  X,
} from 'lucide-react';

import { Button } from '@/shared/ui/button';

import { checkInSchema } from '../schemas/checkin.schema';
import type {
  CheckInFormData,
  CheckInSuccessResult,
  ScheduledCheckInAppointment,
} from '../types/checkin.types';
import { CheckInAppointmentSummary } from './checkin-appointment-summary';
import { CheckInVitalsSection } from './checkin-vitals-section';

interface CheckInModalProps {
  isOpen: boolean;
  onClose: () => void;
  appointment: ScheduledCheckInAppointment | null;
  onSuccess?: (result: CheckInSuccessResult) => void;
}

export function CheckInModal({
  isOpen,
  onClose,
  appointment,
  onSuccess,
}: CheckInModalProps) {
  const [formData, setFormData] = useState<CheckInFormData>({
    reasonForVisit: '',
    isIdentityVerified: false,
    isInsuranceVerified: false,
    priority: 'NORMAL',
  });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [successResult, setSuccessResult] = useState<CheckInSuccessResult | null>(null);

  if (!isOpen || !appointment) return null;

  const handleFieldChange = (partial: Partial<CheckInFormData>) => {
    setFormData((prev) => ({ ...prev, ...partial }));
    // Clear error for changed keys
    setErrors((prev) => {
      const next = { ...prev };
      for (const key of Object.keys(partial)) {
        delete next[key];
      }
      return next;
    });
  };

  const handleSubmit = () => {
    const parseResult = checkInSchema.safeParse(formData);

    if (!parseResult.success) {
      const fieldErrors: Record<string, string> = {};
      for (const issue of parseResult.error.issues) {
        const path = issue.path[0] as string;
        if (path && !fieldErrors[path]) {
          fieldErrors[path] = issue.message;
        }
      }
      setErrors(fieldErrors);
      return;
    }

    setIsSubmitting(true);

    // Giả lập mutation tiếp nhận (M2 contract)
    setTimeout(() => {
      const result: CheckInSuccessResult = {
        visitId: `VIS-${Date.now().toString().slice(-6)}`,
        ticketNo: 'A018',
        patientName: appointment.patientName,
        roomName: appointment.roomName,
        doctorName: appointment.doctorName,
        checkedInAt: new Date().toLocaleTimeString('vi-VN', {
          hour: '2-digit',
          minute: '2-digit',
        }),
      };

      setIsSubmitting(false);
      setSuccessResult(result);
      onSuccess?.(result);
    }, 600);
  };

  const handleClose = () => {
    setFormData({
      reasonForVisit: '',
      isIdentityVerified: false,
      isInsuranceVerified: false,
      priority: 'NORMAL',
    });
    setErrors({});
    setSuccessResult(null);
    onClose();
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="checkin-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity"
        onClick={handleClose}
        aria-hidden="true"
      />

      {/* Modal Dialog */}
      <div className="relative w-full max-w-2xl bg-white rounded-3xl shadow-2xl border border-border overflow-hidden z-10 flex flex-col max-h-[90vh] animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="bg-gradient-to-r from-emerald-50 via-white to-emerald-50/50 p-6 border-b border-border flex items-start justify-between">
          <div className="flex items-center gap-3.5">
            <div className="w-12 h-12 rounded-2xl bg-emerald-100 text-emerald-700 border border-emerald-200 flex items-center justify-center shadow-xs">
              <UserCheck className="w-6 h-6" aria-hidden="true" />
            </div>
            <div>
              <div className="text-xs font-bold text-emerald-700 tracking-wide uppercase">
                Tiếp Nhận Ngoại Trú • Quầy Lễ Tân
              </div>
              <h2
                id="checkin-modal-title"
                className="text-lg font-bold text-ink mt-0.5"
              >
                Xác Nhận Check-in Lịch Hẹn Khám
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={handleClose}
            aria-label="Đóng hộp thoại check-in"
            className="p-2 rounded-xl text-sub hover:text-ink hover:bg-slate-100 transition-colors"
          >
            <X className="w-5 h-5" aria-hidden="true" />
          </button>
        </div>

        {/* Content Body */}
        <div className="p-6 overflow-y-auto space-y-6">
          {successResult ? (
            /* Success Screen */
            <div
              data-testid="checkin-success-view"
              className="py-8 text-center space-y-5 animate-in fade-in zoom-in-95 duration-150"
            >
              <div className="w-16 h-16 rounded-3xl bg-emerald-100 text-emerald-600 border border-emerald-200 flex items-center justify-center mx-auto shadow-xs">
                <CheckCircle2 className="w-9 h-9" aria-hidden="true" />
              </div>

              <div className="space-y-1">
                <span className="text-xs font-bold text-emerald-700 uppercase tracking-wider">
                  Check-in Thành Công
                </span>
                <h3 className="text-2xl font-black text-slate-900">
                  Số Thứ Tự: <span className="font-mono text-primary">{successResult.ticketNo}</span>
                </h3>
                <p className="text-xs text-slate-500">
                  Đã kích hoạt Lượt khám: <span className="font-mono font-bold text-slate-700">{successResult.visitId}</span>
                </p>
              </div>

              {/* Ticket Card */}
              <div className="max-w-md mx-auto p-5 rounded-2xl bg-canvas border border-border text-left text-xs space-y-2">
                <div className="flex justify-between py-1 border-b border-slate-200/60">
                  <span className="text-slate-500">Bệnh nhân:</span>
                  <span className="font-bold text-slate-900">{successResult.patientName}</span>
                </div>
                <div className="flex justify-between py-1 border-b border-slate-200/60">
                  <span className="text-slate-500">Phòng khám:</span>
                  <span className="font-bold text-emerald-700">{successResult.roomName}</span>
                </div>
                <div className="flex justify-between py-1 border-b border-slate-200/60">
                  <span className="text-slate-500">Bác sĩ khám:</span>
                  <span className="font-bold text-slate-800">{successResult.doctorName}</span>
                </div>
                <div className="flex justify-between py-1">
                  <span className="text-slate-500">Thời gian check-in:</span>
                  <span className="font-mono text-slate-700">{successResult.checkedInAt}</span>
                </div>
              </div>

              <div className="flex items-center justify-center gap-3 pt-2">
                <Button variant="secondary" size="sm" onClick={() => window.print()} className="gap-2">
                  <Printer className="w-4 h-4" aria-hidden="true" />
                  In phiếu tiếp nhận (STT)
                </Button>
                <Button variant="primary" size="sm" onClick={handleClose}>
                  Hoàn tất & Đóng
                </Button>
              </div>
            </div>
          ) : (
            /* Form screen */
            <>
              {/* Summary of scheduled appointment */}
              <CheckInAppointmentSummary appointment={appointment} />

              {/* Vitals and reason for visit */}
              <CheckInVitalsSection
                formData={formData}
                onChange={handleFieldChange}
                errors={errors}
              />
            </>
          )}
        </div>

        {/* Footer (only when not in success view) */}
        {!successResult && (
          <div className="p-5 border-t border-border bg-slate-50 flex items-center justify-between gap-3">
            <div className="text-xs text-sub hidden sm:flex items-center gap-1.5">
              <Sparkles className="w-3.5 h-3.5 text-amber-500" aria-hidden="true" />
              <span>Kiểm tra kỹ CCCD/BHYT trước khi kích hoạt STT</span>
            </div>
            <div className="flex items-center gap-3 w-full sm:w-auto justify-end">
              <Button variant="secondary" size="sm" onClick={handleClose}>
                Hủy bỏ
              </Button>
              <Button
                variant="primary"
                size="sm"
                loading={isSubmitting}
                onClick={handleSubmit}
                className="gap-2"
              >
                <CheckCircle2 className="w-4 h-4" aria-hidden="true" />
                Xác nhận Check-in & Cấp STT
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
