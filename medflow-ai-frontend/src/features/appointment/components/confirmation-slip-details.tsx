import { ClipboardPlus, MapPin } from 'lucide-react';

import type { AppointmentListItem } from '../types/appointment-list.types';

interface ConfirmationSlipDetailsProps {
  appointment: AppointmentListItem;
}

export function ConfirmationSlipDetails({ appointment }: ConfirmationSlipDetailsProps) {
  const formattedFee = `${new Intl.NumberFormat('vi-VN').format(appointment.listedFee)} VNĐ`;
  const patientMetaLine = `${appointment.patientBirthDateLabel ?? appointment.patientAgeLabel}${
    appointment.patientPhone ? ` • SĐT: ${appointment.patientPhone}` : ''
  }`;

  return (
    <section
      aria-label="Chi tiết phiếu hẹn khám ngoại trú"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <ClipboardPlus className="h-5 w-5" aria-hidden="true" />
          </div>
          <h2 className="text-sm font-bold text-ink uppercase">
            Chi Tiết Phiếu Hẹn Khám Ngoại Trú
          </h2>
        </div>

        <span className="inline-flex items-center rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-bold text-primary">
          Khám Tự Nguyện
        </span>
      </div>

      <div className="mt-5 grid gap-6 border-b border-border pb-5 sm:grid-cols-2">
        <div className="space-y-4">
          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              Họ và tên người bệnh
            </p>
            <p className="mt-1 text-lg font-bold text-ink">{appointment.patientName}</p>
            <p className="mt-0.5 text-xs text-sub">{patientMetaLine}</p>
            <p className="mt-1 font-mono text-xs font-semibold text-primary">
              Mã bệnh nhân (PID): {appointment.patientCode}
            </p>
          </div>

          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              Dịch vụ đăng ký
            </p>
            <p className="mt-1 text-sm font-bold text-ink">
              {appointment.serviceName} (Mã DV:{' '}
              <span className="font-mono">{appointment.serviceCode}</span>)
            </p>
            {appointment.serviceSubLabel && (
              <p className="mt-0.5 text-xs text-sub">{appointment.serviceSubLabel}</p>
            )}
          </div>
        </div>

        <div className="space-y-4">
          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              Bác sĩ phụ trách
            </p>
            <p className="mt-1 text-lg font-bold text-primary">{appointment.doctorName}</p>
            {appointment.doctorRoleLabel && (
              <p className="mt-0.5 text-xs font-medium text-ink">{appointment.doctorRoleLabel}</p>
            )}
            <p className="mt-0.5 text-xs text-sub">{appointment.departmentName}</p>
          </div>

          <div>
            <p className="text-[11px] font-bold tracking-wide text-sub uppercase">
              Địa điểm buồng khám
            </p>
            <p className="mt-1 flex items-center gap-1.5 text-sm font-bold text-ink">
              <MapPin className="h-4 w-4 shrink-0 text-rose-500" aria-hidden="true" />
              <span>{appointment.roomName}</span>
            </p>
            <p className="mt-0.5 text-xs text-sub">{appointment.roomZoneLabel}</p>
          </div>
        </div>
      </div>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <div className="rounded-xl border border-border bg-canvas p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-xs font-bold text-sub">Chi phí dịch vụ niêm yết:</span>
            <span className="rounded-md bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800 uppercase">
              CHẾ ĐỘ TỰ NGUYỆN, KHÔNG ÁP DỤNG BHYT
            </span>
          </div>
          <p className="mt-2 text-2xl font-extrabold text-primary">{formattedFee}</p>
          <p className="mt-1 text-xs text-sub">{appointment.feePolicyNote}</p>
        </div>

        <div className="rounded-xl border border-border bg-canvas p-4">
          <p className="text-xs font-bold text-sub">Trạng thái thanh toán:</p>
          <p className="mt-2 flex items-center gap-2 text-sm font-bold text-ink">
            <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-amber-500" aria-hidden="true" />
            <span>{appointment.paymentStatusLabel}</span>
          </p>
          {appointment.paymentSubNote && (
            <p className="mt-1 text-xs text-sub">{appointment.paymentSubNote}</p>
          )}
        </div>
      </div>
    </section>
  );
}
