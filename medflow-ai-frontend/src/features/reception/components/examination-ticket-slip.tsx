import { Building2, Clock, MapPin, Stethoscope, UserRound } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { ExaminationTicketDto } from '../types/visit-ticket.types';

interface ExaminationTicketSlipProps {
  ticket: ExaminationTicketDto;
}

export function ExaminationTicketSlip({ ticket }: ExaminationTicketSlipProps) {
  const isWalkIn = ticket.visitType === 'WALK_IN';

  return (
    <article
      aria-label="Phiếu khám bệnh ngoại trú"
      className="rounded-2xl border border-border bg-white p-5 shadow-sm print:border-0 print:shadow-none"
    >
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-start gap-3">
          <div
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary text-white"
            aria-hidden="true"
          >
            <Building2 className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <p className="text-xs font-extrabold uppercase tracking-wide text-navy">
              {ticket.hospitalName}
            </p>
            <p className="text-[11px] text-sub">
              Mã CSYT: <span className="font-mono font-semibold">{ticket.hospitalCode}</span>
              {ticket.hospitalAddress ? ` • ${ticket.hospitalAddress}` : ''}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge tone={isWalkIn ? 'rose' : 'sky'}>{ticket.visitTypeLabel}</Badge>
          <Badge tone={ticket.isReprint ? 'amber' : 'emerald'}>
            {ticket.isReprint
              ? `In lại (Lần ${ticket.printCount})`
              : ticket.printCount > 0
                ? 'Đã in bản gốc'
                : 'Chưa in'}
          </Badge>
        </div>
      </div>

      {/* Hero Queue & Room Banner */}
      <div className="mt-4 grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
        <div className="text-center sm:border-r sm:border-border sm:pr-3 sm:text-left">
          <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
            Số thứ tự khám bệnh
          </p>
          <p className="mt-1 font-mono text-4xl font-extrabold text-primary">{ticket.ticketNo}</p>
          <p className="mt-1 text-[11px] text-sub">
            STT:{' '}
            <span className="font-mono font-semibold text-ink">{ticket.queueNumberFormatted}</span>{' '}
            • {ticket.priorityLabel}
          </p>
        </div>

        <div className="sm:col-span-2 sm:pl-2">
          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
            <div>
              <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
                Buồng khám (UC27)
              </p>
              <p className="mt-0.5 text-sm font-bold text-navy">
                {ticket.roomName}{' '}
                <span className="font-mono text-xs text-primary">({ticket.roomCode})</span>
              </p>
              <p className="mt-0.5 inline-flex items-center gap-1 text-[11px] text-sub">
                <MapPin className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
                <span>{ticket.roomLocation ?? ticket.departmentName}</span>
              </p>
            </div>

            <div>
              <p className="text-[10px] font-bold uppercase tracking-wider text-sub">
                Bác sĩ phụ trách (UC26)
              </p>
              <p className="mt-0.5 text-sm font-bold text-navy">{ticket.doctorName}</p>
              <p className="mt-0.5 inline-flex items-center gap-1 text-[11px] text-sub">
                <Stethoscope className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
                <span>
                  {ticket.specialtyName} •{' '}
                  <span className="font-mono font-semibold">{ticket.doctorCode}</span>
                </span>
              </p>
            </div>
          </div>

          <div className="mt-2.5 flex flex-wrap items-center gap-3 border-t border-border pt-2 text-[11px] text-sub">
            <span>
              Mã lượt khám: <strong className="font-mono text-ink">{ticket.visitCode}</strong>
            </span>
            <span>
              Mã phiếu: <strong className="font-mono text-ink">{ticket.ticketCode}</strong>
            </span>
            <span className="inline-flex items-center gap-1">
              <Clock className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              Đang chờ trước: <strong className="text-ink">{ticket.waitingAheadCount}</strong> lượt
              (~{ticket.estimatedWaitMinutes} phút)
            </span>
          </div>
        </div>
      </div>

      {/* Patient & Visit Info */}
      <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <section aria-label="Thông tin bệnh nhân" className="rounded-xl border border-border p-3.5">
          <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-sub">
            <UserRound className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Thông tin người bệnh</span>
          </div>
          <dl className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <div className="col-span-2">
              <dt className="text-[10px] text-sub">Họ và tên</dt>
              <dd className="font-bold text-navy uppercase">{ticket.patientName}</dd>
            </div>
            <div>
              <dt className="text-[10px] text-sub">Mã bệnh nhân</dt>
              <dd className="font-mono font-semibold text-primary">{ticket.patientCode}</dd>
            </div>
            <div>
              <dt className="text-[10px] text-sub">Giới tính / Tuổi</dt>
              <dd className="font-semibold text-ink">
                {ticket.patientGenderLabel}
                {ticket.patientAge !== null ? ` • ${ticket.patientAge} tuổi` : ''}
              </dd>
            </div>
            {ticket.patientPhone && (
              <div className="col-span-2">
                <dt className="text-[10px] text-sub">Số điện thoại</dt>
                <dd className="font-mono font-semibold text-ink">{ticket.patientPhone}</dd>
              </div>
            )}
          </dl>
        </section>

        <section
          aria-label="Thông tin chuyên môn"
          className="rounded-xl border border-border p-3.5"
        >
          <div className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-sub">
            <Stethoscope className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
            <span>Thông tin tiếp nhận khám</span>
          </div>
          <dl className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <div className="col-span-2">
              <dt className="text-[10px] text-sub">Dịch vụ khám</dt>
              <dd className="font-semibold text-navy">{ticket.serviceName}</dd>
            </div>
            <div>
              <dt className="text-[10px] text-sub">Khoa</dt>
              <dd className="font-semibold text-ink">{ticket.departmentName}</dd>
            </div>
            <div>
              <dt className="text-[10px] text-sub">Giờ tiếp nhận</dt>
              <dd className="font-mono font-semibold text-ink">
                {ticket.checkedInAtLabel ?? ticket.queueDate}
              </dd>
            </div>
            {ticket.reasonForVisit && (
              <div className="col-span-2">
                <dt className="text-[10px] text-sub">Lý do khám</dt>
                <dd className="font-medium text-ink">{ticket.reasonForVisit}</dd>
              </div>
            )}
          </dl>
        </section>
      </div>

      {/* Guidance Footer */}
      <div className="mt-4 rounded-xl border border-sky-200 bg-sky-50/70 px-3.5 py-2.5 text-xs text-navy">
        <p className="font-semibold">{ticket.guidanceNote}</p>
        <p className="mt-1 text-[11px] text-sub">
          Nhân viên in phiếu: <strong className="text-ink">{ticket.printedByStaffName}</strong> (
          <span className="font-mono">{ticket.printedByStaffCode}</span>) • Thời điểm in:{' '}
          <span className="font-mono">{ticket.printedAtLabel}</span> • Số bản:{' '}
          <strong className="font-mono">{ticket.copies}</strong>
        </p>
      </div>
    </article>
  );
}
