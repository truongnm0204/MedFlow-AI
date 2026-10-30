import {
  ArrowRight,
  Building2,
  Calendar,
  CheckCircle2,
  Clock,
  FileText,
  Hourglass,
  Info,
  QrCode,
  ReceiptText,
  RotateCcw,
  X,
  XCircle,
} from 'lucide-react';

import { CANCELLED_STATUSES, COMPLETED_STATUSES } from '../data/appointment-list-mock';
import type { AppointmentListItem } from '../types/appointment-list.types';

interface AppointmentCardProps {
  appointment: AppointmentListItem;
  onViewTicketModal: (appointment: AppointmentListItem) => void;
  onReschedule?: (appointment: AppointmentListItem) => void;
  onCancelRequest?: (appointment: AppointmentListItem) => void;
  onRebookSpecialty?: (appointment: AppointmentListItem) => void;
}

export function AppointmentCard({
  appointment,
  onViewTicketModal,
  onReschedule,
  onCancelRequest,
  onRebookSpecialty,
}: AppointmentCardProps) {
  const isCompleted = COMPLETED_STATUSES.includes(appointment.status);
  const isCancelled = CANCELLED_STATUSES.includes(appointment.status);

  if (isCompleted) {
    return (
      <article
        aria-label={`Lịch hẹn ${appointment.appointmentCode}`}
        className="rounded-2xl border border-border bg-white p-5 shadow-xs transition-shadow hover:shadow-sm"
      >
        {/* Completed Header */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3.5">
          <div className="flex flex-wrap items-center gap-2.5">
            <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-0.5 text-[11px] font-bold text-emerald-700">
              <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{appointment.statusBannerLabel}</span>
            </span>
            <span className="font-mono text-sm font-bold text-ink">
              #{appointment.appointmentCode}
            </span>
            <span className="text-xs text-sub">• Khám ngày {appointment.scheduledDateBadge}</span>
          </div>

          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50/80 px-3 py-1 text-[11px] font-semibold text-emerald-700">
            <ReceiptText className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{appointment.paymentStatusLabel}</span>
          </span>
        </div>

        {/* Completed Body */}
        <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-12">
          <div className="lg:col-span-4">
            <p className="text-[11px] font-medium text-sub">Bác sĩ &amp; Chuyên khoa</p>
            <p className="mt-0.5 text-sm font-bold text-ink">{appointment.doctorName}</p>
            <p className="mt-0.5 text-xs text-sub">
              {appointment.specialtyName} • Người khám: {appointment.patientName}
            </p>
          </div>

          <div className="lg:col-span-3">
            <p className="text-[11px] font-medium text-sub">Thời gian &amp; Địa điểm</p>
            <p className="mt-0.5 text-sm font-bold text-ink">
              {appointment.scheduledTimeRange} • {appointment.scheduledDateBadge}
            </p>
            <p className="mt-0.5 text-xs text-sub">{appointment.roomName}</p>
          </div>

          <div className="rounded-xl border border-slate-100 bg-canvas p-3 lg:col-span-5">
            <p className="flex items-center gap-1.5 text-xs font-bold text-ink">
              <FileText className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Kết luận lâm sàng tóm tắt:</span>
            </p>
            <p className="mt-1 text-xs leading-relaxed text-sub">
              {appointment.clinicalSummary ?? 'Đã hoàn tất khám và lưu hồ sơ bệnh án ngoại trú.'}
            </p>
          </div>
        </div>

        {/* Completed Footer */}
        <div className="mt-3.5 flex items-center justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={() => onViewTicketModal(appointment)}
            className="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-bold text-primary transition-colors hover:bg-sky-50"
          >
            <FileText className="h-3.5 w-3.5" aria-hidden="true" />
            <span>Xem hồ sơ kết quả</span>
          </button>
          <button
            type="button"
            onClick={() => onViewTicketModal(appointment)}
            aria-label={`Biên lai lịch hẹn ${appointment.appointmentCode}`}
            className="rounded-lg border border-border bg-canvas p-1.5 text-sub transition-colors hover:bg-slate-100 hover:text-ink"
          >
            <ReceiptText className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </article>
    );
  }

  if (isCancelled) {
    return (
      <article
        aria-label={`Lịch hẹn ${appointment.appointmentCode}`}
        className="rounded-2xl border border-border bg-white p-5 shadow-xs transition-shadow hover:shadow-sm"
      >
        {/* Cancelled Header */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3.5">
          <div className="flex flex-wrap items-center gap-2.5">
            <span className="inline-flex items-center gap-1.5 rounded-full border border-rose-200 bg-rose-50 px-3 py-0.5 text-[11px] font-bold text-rose-700">
              <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{appointment.statusBannerLabel}</span>
            </span>
            <span className="font-mono text-sm font-bold text-sub">
              #{appointment.appointmentCode}
            </span>
            <span className="text-xs text-sub">
              • Ngày hẹn cũ: {appointment.scheduledDateBadge} ({appointment.scheduledTimeRange})
            </span>
          </div>

          <span className="text-xs font-medium text-sub">
            {appointment.slotReleaseNote ?? appointment.paymentStatusLabel}
          </span>
        </div>

        {/* Cancelled Body */}
        <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-12">
          <div className="lg:col-span-4">
            <p className="text-[11px] font-medium text-sub">Bác sĩ dự kiến</p>
            <p className="mt-0.5 text-sm font-bold text-ink">{appointment.doctorName}</p>
            <p className="mt-0.5 text-xs text-sub">
              {appointment.specialtyName} • Người khám: {appointment.patientName}
            </p>
          </div>

          <div className="rounded-xl border border-slate-100 bg-canvas p-3 lg:col-span-8">
            <p className="flex items-center gap-1.5 text-xs font-bold text-sub">
              <Info className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <span>Lý do hủy lịch:</span>
            </p>
            <p className="mt-1 text-xs leading-relaxed text-sub">
              {appointment.cancellationReason ?? 'Người bệnh chủ động hủy lịch hẹn trước giờ khám.'}
            </p>
          </div>
        </div>

        {/* Cancelled Footer */}
        <div className="mt-3.5 flex items-center justify-end pt-1">
          <button
            type="button"
            onClick={() => onRebookSpecialty?.(appointment)}
            className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-bold text-ink shadow-2xs transition-colors hover:bg-canvas"
          >
            <RotateCcw className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>
              {appointment.rebookSpecialtyLabel ?? `Đặt lại ${appointment.specialtyName}`}
            </span>
          </button>
        </div>
      </article>
    );
  }

  // Default: UPCOMING / CONFIRMED Card (Figma 17:676 Primary Highlight Card)
  return (
    <article
      aria-label={`Lịch hẹn ${appointment.appointmentCode}`}
      className="relative overflow-hidden rounded-2xl border border-sky-200 bg-white p-6 pl-7 shadow-sm"
    >
      {/* Left Sky Brand Accent Bar */}
      <div className="absolute inset-y-0 left-0 w-1.5 bg-primary" aria-hidden="true" />

      {/* Upcoming Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 pb-4">
        <div className="flex flex-wrap items-center gap-2.5">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[11px] font-bold text-emerald-700">
            <span className="h-2 w-2 rounded-full bg-emerald-500" aria-hidden="true" />
            <span>{appointment.statusBannerLabel}</span>
          </span>
          <span className="font-mono text-base font-bold text-ink">
            #{appointment.appointmentCode}
          </span>
          <span className="rounded-md bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-sub">
            ({appointment.scheduledDateBadge})
          </span>
        </div>

        {appointment.queueNumber && (
          <div className="flex items-center gap-2 rounded-xl border border-sky-200 bg-sky-50/70 px-4 py-1.5">
            <span className="text-[11px] font-bold tracking-wider text-sub uppercase">
              SỐ THỨ TỰ:
            </span>
            <span className="font-mono text-xl font-extrabold text-primary">
              {appointment.queueNumber}
            </span>
          </div>
        )}
      </div>

      {/* Upcoming 3-Column Body */}
      <div className="mt-5 grid grid-cols-1 gap-6 lg:grid-cols-12">
        {/* Column 1: Time, Room & Amber Reception Notice */}
        <div className="space-y-4 lg:col-span-4">
          <div className="flex items-start gap-3">
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sky-50 text-primary">
              <Clock className="h-4 w-4" aria-hidden="true" />
            </div>
            <div>
              <p className="text-[11px] font-medium text-sub">Khung giờ tiếp đón</p>
              <p className="text-sm font-bold text-ink">{appointment.scheduledTimeRange}</p>
              <p className="text-xs font-semibold text-primary">
                {appointment.scheduledDateFullLabel}
              </p>
            </div>
          </div>

          <div className="flex items-start gap-3">
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-sub">
              <Building2 className="h-4 w-4" aria-hidden="true" />
            </div>
            <div>
              <p className="text-[11px] font-medium text-sub">Buồng khám chỉ định</p>
              <p className="text-sm font-bold text-ink">{appointment.roomName}</p>
              <p className="text-xs text-sub">{appointment.roomZoneLabel}</p>
            </div>
          </div>

          {appointment.checkInDeadlineTime && (
            <div className="flex items-start gap-2 rounded-xl border border-amber-200/90 bg-amber-50/80 p-3 text-xs leading-relaxed text-amber-900">
              <Info className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-600" aria-hidden="true" />
              <p>
                Vui lòng có mặt lúc{' '}
                <strong className="font-bold">{appointment.checkInDeadlineTime}</strong> tại{' '}
                {appointment.checkInCounterLabel ?? 'Quầy Tiếp đón Tầng 1 - Nhà A'} để nhận diện và
                in phiếu vào buồng khám.
              </p>
            </div>
          )}
        </div>

        {/* Column 2: Patient, Doctor & Service */}
        <div className="space-y-4 lg:col-span-4">
          <div>
            <p className="text-[11px] font-medium text-sub">Người thực hiện khám</p>
            <p className="mt-0.5 text-sm font-bold text-ink">{appointment.patientName}</p>
            <p className="text-xs text-sub">
              {appointment.patientRelationshipLabel}, {appointment.patientAgeLabel} • PID:{' '}
              <span className="font-mono font-semibold text-ink">{appointment.patientCode}</span>
            </p>
          </div>

          <div>
            <p className="text-[11px] font-medium text-sub">Bác sĩ phụ trách</p>
            <p className="mt-0.5 text-sm font-bold text-ink">{appointment.doctorName}</p>
            <p className="text-xs text-sub">{appointment.doctorRoleLabel}</p>
          </div>

          <div>
            <p className="text-[11px] font-medium text-sub">Dịch vụ chỉ định</p>
            <div className="mt-1 inline-block rounded-lg border border-border bg-canvas px-3 py-1.5 text-xs font-semibold text-ink">
              {appointment.serviceName} (Mã:{' '}
              <span className="font-mono">{appointment.serviceCode}</span>)
            </div>
          </div>
        </div>

        {/* Column 3: Listed Fee & Payment Status */}
        <div className="rounded-xl border border-slate-100 bg-canvas/70 p-4 lg:col-span-4">
          <p className="text-[11px] font-medium text-sub">Phí dịch vụ ngoại trú niêm yết</p>
          <p className="mt-1 text-xl font-extrabold text-ink">
            {appointment.listedFee.toLocaleString('vi-VN')}{' '}
            <span className="text-xs font-bold text-sub">VNĐ</span>
          </p>
          <p className="mt-1 flex items-center gap-1.5 text-xs font-semibold text-primary">
            <CheckCircle2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>{appointment.feePolicyNote}</span>
          </p>

          <div className="mt-4 border-t border-slate-200/70 pt-3">
            <p className="text-[11px] font-medium text-sub">Trạng thái thanh toán</p>
            <p className="mt-0.5 flex items-center gap-1.5 text-xs font-bold text-amber-700">
              <Hourglass className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <span>{appointment.paymentStatusLabel}</span>
            </p>
            {appointment.paymentSubNote && (
              <p className="mt-0.5 text-[11px] text-sub">{appointment.paymentSubNote}</p>
            )}
          </div>
        </div>
      </div>

      {/* Upcoming Footer Actions */}
      <div className="mt-5 flex flex-wrap items-center justify-end gap-3 border-t border-slate-100 pt-4">
        <button
          type="button"
          onClick={() => onCancelRequest?.(appointment)}
          className="inline-flex items-center gap-1 rounded-xl px-3 py-2 text-xs font-bold text-danger transition-colors hover:bg-rose-50"
        >
          <X className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Hủy lịch hẹn</span>
        </button>

        <button
          type="button"
          onClick={() => onReschedule?.(appointment)}
          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
        >
          <Calendar className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
          <span>Đổi lịch khám</span>
        </button>

        <button
          type="button"
          onClick={() => onViewTicketModal(appointment)}
          className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white shadow-xs transition-colors hover:bg-primary-dark"
        >
          <QrCode className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Xem chi tiết phiếu hẹn &amp; Mã QR</span>
          <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      </div>
    </article>
  );
}
