import {
  Building2,
  Calendar,
  CheckCircle2,
  Clock,
  Printer,
  QrCode,
  ShieldCheck,
} from 'lucide-react';

import { Modal } from '@/shared/ui/modal';

import type { AppointmentListItem } from '../types/appointment-list.types';

interface AppointmentQuickViewModalProps {
  appointment: AppointmentListItem | null;
  onClose: () => void;
}

export function AppointmentQuickViewModal({
  appointment,
  onClose,
}: AppointmentQuickViewModalProps) {
  if (!appointment) return null;

  return (
    <Modal open={Boolean(appointment)} onClose={onClose} maxWidth="max-w-xl">
      <div className="p-6">
        {/* Header (SCR-APT-07B Slip Preview) */}
        <div className="flex items-start justify-between border-b border-border pb-4 pr-8">
          <div>
            <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-[11px] font-bold text-primary">
              <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
              <span>PHIẾU HẸN KHÁM NGOẠI TRÚ ĐIỆN TỬ (SCR-APT-07B)</span>
            </span>
            <h2 className="mt-2 text-lg font-extrabold text-ink">
              PHIẾU HẸN KHÁM NGOẠI TRÚ &amp; SỐ THỨ TỰ GIỮ TRƯỚC
            </h2>
            <p className="text-xs text-sub">{appointment.feePolicyNote}</p>
          </div>
        </div>

        {/* Code, Queue Number & QR Block */}
        <div className="mt-4 grid grid-cols-1 gap-4 rounded-2xl border border-sky-200 bg-sky-50/50 p-4 sm:grid-cols-3">
          <div>
            <p className="text-[10px] font-bold tracking-wider text-sub uppercase">
              MÃ LỊCH HẸN (CODE)
            </p>
            <p className="mt-1 font-mono text-sm font-extrabold text-ink">
              #{appointment.appointmentCode}
            </p>
            <p className="mt-1 text-[11px] text-sub">
              {appointment.scheduledTimeRange} • {appointment.scheduledDateBadge}
            </p>
          </div>

          <div className="text-center">
            <p className="text-[10px] font-bold tracking-wider text-sub uppercase">
              SỐ THỨ TỰ GIỮ TRƯỚC
            </p>
            <p className="mt-0.5 font-mono text-3xl font-extrabold text-primary">
              {appointment.queueNumber ?? '---'}
            </p>
            <span className="mt-1 inline-block rounded-full bg-primary px-2.5 py-0.5 text-[10px] font-bold text-white">
              {appointment.roomCode}
            </span>
          </div>

          <div className="flex flex-col items-center justify-center rounded-xl border border-border bg-white p-2.5">
            <QrCode className="h-12 w-12 text-ink" aria-hidden="true" />
            <span className="mt-1 text-[10px] font-semibold text-sub">Quét check-in tại Quầy</span>
          </div>
        </div>

        {/* Patient & Clinical Info */}
        <div className="mt-4 grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas/60 p-4 text-xs sm:grid-cols-2">
          <div>
            <p className="text-[11px] font-medium text-sub">Họ và tên người khám</p>
            <p className="font-bold text-ink">
              {appointment.patientName} ({appointment.patientRelationshipLabel})
            </p>
          </div>
          <div>
            <p className="text-[11px] font-medium text-sub">Mã bệnh nhân (PID)</p>
            <p className="font-mono font-bold text-primary">{appointment.patientCode}</p>
          </div>
          <div>
            <p className="text-[11px] font-medium text-sub">Bác sĩ &amp; Chuyên khoa</p>
            <p className="font-bold text-ink">{appointment.doctorName}</p>
            <p className="text-sub">{appointment.departmentName}</p>
          </div>
          <div>
            <p className="text-[11px] font-medium text-sub">Buồng khám &amp; Dịch vụ</p>
            <p className="font-bold text-ink">{appointment.roomName}</p>
            <p className="text-sub">
              {appointment.serviceName} (
              <span className="font-mono">{appointment.serviceCode}</span>)
            </p>
          </div>
        </div>

        {/* Fee & Notice */}
        <div className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white px-4 py-3 text-xs">
          <div className="flex items-center gap-2 text-sub">
            <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
            <span>{appointment.paymentStatusLabel}</span>
          </div>
          <div className="font-bold text-ink">
            Giá dịch vụ: {appointment.listedFee.toLocaleString('vi-VN')} VNĐ
          </div>
        </div>

        {appointment.clinicalSummary && (
          <div className="mt-3 rounded-xl border border-emerald-200 bg-emerald-50/70 p-3 text-xs text-emerald-900">
            <p className="flex items-center gap-1.5 font-bold">
              <CheckCircle2 className="h-4 w-4 text-emerald-600" aria-hidden="true" />
              <span>Kết luận lâm sàng tóm tắt:</span>
            </p>
            <p className="mt-1">{appointment.clinicalSummary}</p>
          </div>
        )}

        {/* Modal Footer */}
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
          <div className="flex items-center gap-2 text-[11px] text-sub">
            <Building2 className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Xuất trình tại Quầy Tiếp đón Tầng 1 trước giờ khám 15 phút</span>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => window.print()}
              className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-bold text-ink transition-colors hover:bg-canvas"
            >
              <Printer className="h-3.5 w-3.5" aria-hidden="true" />
              <span>In Phiếu Ngay</span>
            </button>
            <button
              type="button"
              onClick={onClose}
              className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white transition-colors hover:bg-primary-dark"
            >
              <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
              <span>Đóng phiếu hẹn</span>
            </button>
          </div>
        </div>
      </div>
    </Modal>
  );
}
