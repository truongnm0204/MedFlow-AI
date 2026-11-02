import { Clock, Info, Plus } from 'lucide-react';

import type { AppointmentListItem } from '../types/appointment-list.types';
import { AppointmentCheckinQr } from './appointment-checkin-qr';

interface AppointmentPrintSlipProps {
  appointment: AppointmentListItem;
  printedAtLabel: string;
}

export function AppointmentPrintSlip({ appointment, printedAtLabel }: AppointmentPrintSlipProps) {
  const formattedFee = `${new Intl.NumberFormat('vi-VN').format(appointment.listedFee)} VNĐ`;
  const counterLabel = appointment.checkInCounterLabel ?? 'Quầy Tiếp đón Tầng 1 - Nhà A';
  const deadlineTime = appointment.checkInDeadlineTime ?? '--:--';
  const patientFields = [
    { label: 'Họ và tên', value: appointment.patientName, cls: 'font-bold text-ink uppercase' },
    {
      label: 'Mã người bệnh (PID)',
      value: appointment.patientCode,
      cls: 'font-mono font-bold text-primary',
    },
    {
      label: 'Ngày sinh & Tuổi',
      value: appointment.patientBirthDateLabel ?? appointment.patientAgeLabel,
      cls: 'font-semibold text-ink',
    },
    appointment.patientPhone
      ? { label: 'Số điện thoại', value: appointment.patientPhone, cls: 'font-semibold text-ink' }
      : null,
    appointment.patientNationalId
      ? {
          label: 'Số CCCD / Định danh',
          value: appointment.patientNationalId,
          cls: 'font-mono font-semibold text-ink',
        }
      : null,
    appointment.patientAddress
      ? {
          label: 'Địa chỉ cư trú',
          value: appointment.patientAddress,
          cls: 'font-semibold text-ink',
          span: 'sm:col-span-3',
        }
      : null,
  ].filter((item): item is NonNullable<typeof item> => item !== null);

  return (
    <article
      aria-label="Phiếu hẹn khám ngoại trú A5"
      className="rounded-2xl bg-white p-8 shadow-xl print:rounded-none print:shadow-none"
    >
      <div className="flex items-start justify-between gap-4 border-b-2 border-ink pb-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary text-white">
            <Plus className="h-6 w-6" strokeWidth={2.5} aria-hidden="true" />
          </div>
          <div className="text-xs leading-relaxed">
            <p className="text-sm font-extrabold text-ink">BỆNH VIỆN ĐA KHOA QUỐC TẾ MEDFLOW</p>
            <p className="text-sub">Địa chỉ: 180 Đường Giải Phóng, Quận Đống Đa, TP. Hà Nội</p>
            <p className="text-sub">Hotline tiếp đón: 1900 6868 • Cấp cứu: 024 3999 8888</p>
          </div>
        </div>
        <p className="shrink-0 text-right text-[11px] font-medium text-sub">
          Thời gian in: {printedAtLabel}
        </p>
      </div>

      <div className="mt-5 text-center">
        <h2 className="text-xl font-extrabold text-ink uppercase">
          PHIẾU HẸN KHÁM NGOẠI TRÚ &amp; SỐ THỨ TỰ GIỮ TRƯỚC
        </h2>
        <p className="mt-1 text-xs text-sub italic">
          (Chế độ Khám Ngoại Trú Tự Nguyện - Không áp dụng BHYT)
        </p>
      </div>

      <div className="mt-5 grid grid-cols-3 divide-x divide-sky-200 rounded-xl border-2 border-dashed border-sky-300 bg-sky-50/50 p-4">
        <div className="pr-3">
          <p className="text-[10px] font-bold tracking-wider text-sub uppercase">
            MÃ LỊCH HẸN (CODE)
          </p>
          <p className="mt-1 font-mono text-base font-extrabold text-primary">
            #{appointment.appointmentCode}
          </p>
          <p className="mt-1.5 flex items-center gap-1 text-[11px] text-sub">
            <Clock className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
            <span>
              {appointment.scheduledDateFullLabel} • {appointment.scheduledTimeRange}
            </span>
          </p>
        </div>
        <div className="px-3 text-center">
          <p className="text-[10px] font-bold tracking-wider text-sub uppercase">
            SỐ THỨ TỰ GIỮ TRƯỚC
          </p>
          <p className="mt-1 font-mono text-5xl font-extrabold text-primary">
            {appointment.queueNumber ?? '---'}
          </p>
          <span className="mt-1.5 inline-block rounded-full bg-primary px-2.5 py-0.5 font-mono text-[10px] font-bold text-white">
            PHÒNG KHÁM: {appointment.roomCode.replace('P.', '')}
          </span>
        </div>
        <div className="flex justify-center pl-3">
          <AppointmentCheckinQr
            appointmentCode={appointment.appointmentCode}
            caption="Quét check-in tại Quầy"
          />
        </div>
      </div>

      <section className="mt-5">
        <div className="flex items-center gap-2">
          <span className="h-4 w-1 rounded bg-primary" aria-hidden="true" />
          <h3 className="text-xs font-extrabold text-ink uppercase">I. THÔNG TIN BỆNH NHÂN:</h3>
        </div>
        <dl className="mt-2.5 grid grid-cols-2 gap-x-4 gap-y-2.5 text-xs sm:grid-cols-4">
          {patientFields.map((f) => (
            <div key={f.label} className={f.span}>
              <dt className="text-[11px] text-sub">{f.label}</dt>
              <dd className={f.cls}>{f.value}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="mt-5">
        <div className="flex items-center gap-2">
          <span className="h-4 w-1 rounded bg-primary" aria-hidden="true" />
          <h3 className="text-xs font-extrabold text-ink uppercase">
            II. THÔNG TIN KHÁM BỆNH &amp; TIẾP NHẬN:
          </h3>
        </div>
        <dl className="mt-2.5 grid grid-cols-2 gap-x-4 gap-y-2.5 text-xs sm:grid-cols-3">
          {[
            {
              label: 'Chuyên khoa tiếp nhận',
              val: appointment.departmentName,
              cls: 'font-bold text-ink',
            },
            { label: 'Bác sĩ thăm khám', val: appointment.doctorName, cls: 'font-bold text-ink' },
            {
              label: 'Buồng khám chỉ định',
              val: appointment.roomName,
              cls: 'font-bold text-primary',
            },
            {
              label: 'Dịch vụ chỉ định',
              val: `${appointment.serviceName} (${appointment.serviceCode})`,
              cls: 'font-semibold text-ink',
            },
            { label: 'Giá dịch vụ niêm yết', val: formattedFee, cls: 'font-extrabold text-ink' },
          ].map((f) => (
            <div key={f.label}>
              <dt className="text-[11px] text-sub">{f.label}</dt>
              <dd className={f.cls}>{f.val}</dd>
            </div>
          ))}
          <div>
            <dt className="text-[11px] text-sub">Tình trạng thanh toán</dt>
            <dd>
              <span className="inline-block rounded-md bg-amber-100 px-2 py-0.5 text-[11px] font-bold text-amber-800">
                {appointment.paymentStatusLabel}
              </span>
            </dd>
          </div>
        </dl>
      </section>

      <section className="mt-5 rounded-xl border border-amber-200 bg-amber-50/60 p-4 text-xs leading-relaxed text-ink">
        <p className="flex items-center gap-1.5 font-extrabold text-amber-900 uppercase">
          <Info className="h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
          <span>LƯU Ý QUAN TRỌNG DÀNH CHO BỆNH NHÂN:</span>
        </p>
        <ol className="mt-2 list-decimal space-y-1.5 pl-4">
          <li>
            <strong>Có mặt đúng giờ:</strong> Quý khách vui lòng có mặt tại {counterLabel} trước giờ
            hẹn 15 phút (lúc {deadlineTime}) để đối chiếu thông tin và nhận phiếu vào phòng khám.
          </li>
          <li>
            <strong>Giấy tờ xuất trình:</strong> Xuất trình phiếu hẹn này (bản in hoặc ảnh chụp trên
            điện thoại) kèm CCCD gắn chip khi làm thủ tục tiếp đón.
          </li>
          <li>
            <strong>Quy định trễ hẹn:</strong> Nếu đến trễ quá 15 phút so với giờ hẹn, số thứ tự sẽ
            được hệ thống tự động chuyển sang hàng đợi điều phối lại để đảm bảo tiến độ ca khám.
          </li>
        </ol>
      </section>

      <footer className="mt-5 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3 text-[11px] text-sub">
        <span>Phiếu in tự động từ Cổng MedFlowAI • Có giá trị trong ngày khám</span>
        <span>Người lập phiếu: Hệ thống MedFlow Tự động</span>
      </footer>
    </article>
  );
}
