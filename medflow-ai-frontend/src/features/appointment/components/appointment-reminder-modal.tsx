import { useState } from 'react';
import {
  AlertCircle,
  Calendar,
  CheckCircle2,
  Clock,
  FileText,
  MapPin,
  Stethoscope,
  X,
} from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { AppointmentReminder } from '../types/appointment-reminder.types';

interface AppointmentReminderModalProps {
  isOpen: boolean;
  onClose: () => void;
  reminder: AppointmentReminder;
  onConfirmAttendance?: (reminderId: string) => void;
}

export function AppointmentReminderModal({
  isOpen,
  onClose,
  reminder,
  onConfirmAttendance,
}: AppointmentReminderModalProps) {
  const [isConfirmed, setIsConfirmed] = useState(reminder.isConfirmedByPatient);
  const [showToast, setShowToast] = useState(false);

  if (!isOpen) return null;

  const handleConfirm = () => {
    setIsConfirmed(true);
    setShowToast(true);
    onConfirmAttendance?.(reminder.id);
    setTimeout(() => {
      setShowToast(false);
    }, 4000);
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="reminder-modal-title"
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
        <div className="bg-gradient-to-r from-sky-50 via-white to-sky-50/50 p-6 border-b border-border flex items-start justify-between">
          <div className="flex items-center gap-3.5">
            <div className="w-12 h-12 rounded-2xl bg-primary/10 text-primary border border-primary/20 flex items-center justify-center shadow-xs">
              <Clock className="w-6 h-6 animate-pulse" aria-hidden="true" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="font-mono text-xs font-bold text-primary tracking-wide">
                  {reminder.appointmentCode}
                </span>
                <Badge tone={reminder.urgency === 'URGENT' ? 'amber' : 'sky'}>
                  {reminder.urgency === 'URGENT'
                    ? `SẮP ĐẾN GIỜ KHÁM • CÒN ${reminder.hoursRemaining} GIỜ`
                    : 'LỊCH HẸN SẮP TỚI'}
                </Badge>
              </div>
              <h2
                id="reminder-modal-title"
                className="text-lg font-bold text-ink mt-1"
              >
                Nhắc Lịch Khám Bệnh Ngoại Trú
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hộp thoại nhắc lịch"
            className="p-2 rounded-xl text-sub hover:text-ink hover:bg-slate-100 transition-colors"
          >
            <X className="w-5 h-5" aria-hidden="true" />
          </button>
        </div>

        {/* Content body */}
        <div className="p-6 overflow-y-auto space-y-6 text-sm">
          {showToast && (
            <div
              role="status"
              className="p-3.5 rounded-2xl bg-emerald-50 border border-emerald-200 text-emerald-800 flex items-center gap-2.5 text-xs font-medium animate-in fade-in slide-in-from-top-2"
            >
              <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" aria-hidden="true" />
              <span>Đã ghi nhận bạn sẽ đến đúng giờ! Bệnh viện đã giữ chỗ khám ưu tiên cho bạn.</span>
            </div>
          )}

          {/* Appointment key facts card */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 p-4.5 rounded-2xl bg-canvas border border-border">
            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-xl bg-sky-100 text-sky-700 flex items-center justify-center shrink-0">
                <Calendar className="w-4 h-4" aria-hidden="true" />
              </div>
              <div>
                <div className="text-xs text-sub font-medium">Thời gian hẹn</div>
                <div className="text-sm font-bold text-ink">
                  {reminder.scheduledTime} • {reminder.scheduledDate}
                </div>
              </div>
            </div>

            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-xl bg-emerald-100 text-emerald-700 flex items-center justify-center shrink-0">
                <MapPin className="w-4 h-4" aria-hidden="true" />
              </div>
              <div>
                <div className="text-xs text-sub font-medium">Phòng khám</div>
                <div className="text-sm font-bold text-ink">{reminder.roomName}</div>
              </div>
            </div>

            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-xl bg-indigo-100 text-indigo-700 flex items-center justify-center shrink-0">
                <Stethoscope className="w-4 h-4" aria-hidden="true" />
              </div>
              <div>
                <div className="text-xs text-sub font-medium">Bác sĩ phụ trách</div>
                <div className="text-sm font-bold text-ink">{reminder.doctorName}</div>
                <div className="text-xs text-sub">{reminder.specialtyName}</div>
              </div>
            </div>

            <div className="flex items-start gap-3">
              <div className="w-9 h-9 rounded-xl bg-amber-100 text-amber-700 flex items-center justify-center shrink-0">
                <AlertCircle className="w-4 h-4" aria-hidden="true" />
              </div>
              <div>
                <div className="text-xs text-sub font-medium">Trạng thái xác nhận</div>
                <div className="text-sm font-bold text-ink">
                  {isConfirmed ? (
                    <span className="text-emerald-700 font-semibold flex items-center gap-1">
                      <CheckCircle2 className="w-3.5 h-3.5" aria-hidden="true" /> Đã xác nhận tham gia
                    </span>
                  ) : (
                    <span className="text-amber-700 font-medium">Chờ người bệnh xác nhận</span>
                  )}
                </div>
              </div>
            </div>
          </div>

          {/* Preparations list */}
          <div>
            <h3 className="text-xs font-bold uppercase tracking-wider text-navy mb-3 flex items-center gap-2">
              <FileText className="w-4 h-4 text-primary" aria-hidden="true" />
              Lưu ý chuẩn bị trước khi đến khám
            </h3>
            <ul className="space-y-2">
              {reminder.preparationNotes.map((note, index) => (
                <li
                  key={index}
                  className="flex items-start gap-2.5 text-xs text-slate-700 bg-white p-3 rounded-xl border border-slate-200/80"
                >
                  <span className="w-5 h-5 rounded-full bg-slate-100 text-slate-600 flex items-center justify-center text-[10px] font-bold shrink-0 mt-0.5">
                    {index + 1}
                  </span>
                  <span className="leading-relaxed">{note}</span>
                </li>
              ))}
            </ul>
          </div>

          {/* Documents required */}
          <div>
            <h3 className="text-xs font-bold uppercase tracking-wider text-navy mb-3 flex items-center gap-2">
              <CheckCircle2 className="w-4 h-4 text-emerald-600" aria-hidden="true" />
              Giấy tờ cần mang theo tại quầy tiếp đón
            </h3>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
              {reminder.documentsRequired.map((doc, idx) => (
                <div
                  key={idx}
                  className="p-3 rounded-xl bg-slate-50 border border-border text-xs text-slate-800 font-medium flex items-center gap-2"
                >
                  <div className="w-2 h-2 rounded-full bg-primary shrink-0" aria-hidden="true" />
                  <span>{doc}</span>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="p-5 border-t border-border bg-slate-50 flex items-center justify-between gap-3">
          <div className="text-xs text-sub hidden sm:block">
            Nhận thông báo lúc: {reminder.notifiedAt}
          </div>
          <div className="flex items-center gap-3 w-full sm:w-auto justify-end">
            <Button variant="secondary" size="sm" onClick={onClose}>
              Đóng
            </Button>
            <Button
              variant="primary"
              size="sm"
              disabled={isConfirmed}
              onClick={handleConfirm}
              className="gap-2"
            >
              <CheckCircle2 className="w-4 h-4" aria-hidden="true" />
              {isConfirmed ? 'Đã xác nhận có mặt' : 'Xác nhận sẽ đến khám'}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
