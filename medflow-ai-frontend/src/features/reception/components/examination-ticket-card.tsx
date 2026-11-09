import type { ReactNode } from 'react';
import { QrCode } from 'lucide-react';

import type {
  ExaminationTicketData,
  ExaminationTicketGender,
} from '../types/examination-ticket.types';

interface ExaminationTicketCardProps {
  ticket: ExaminationTicketData;
}

const GENDER_LABEL: Record<ExaminationTicketGender, string> = {
  MALE: 'Nam',
  FEMALE: 'Nữ',
  OTHER: 'Khác',
};

/** Độ rộng (px) lặp lại để mô phỏng vạch mã vạch, suy ra từ giá trị mã. */
function getBarWidths(value: string): number[] {
  return Array.from(value).map((ch) => (ch.charCodeAt(0) % 3) + 1);
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-3 text-xs">
      <dt className="text-sub">{label}</dt>
      <dd className="text-right font-semibold text-navy">{children}</dd>
    </div>
  );
}

export function ExaminationTicketCard({ ticket }: ExaminationTicketCardProps) {
  const bars = getBarWidths(ticket.barcodeValue);

  return (
    <article
      id="examination-ticket-print-area"
      aria-label="Phiếu khám bệnh vào phòng"
      className="mx-auto w-full max-w-sm bg-white p-6 text-ink"
    >
      <header className="text-center">
        <p className="text-[11px] font-bold uppercase tracking-wide text-navy">
          {ticket.hospitalName}
        </p>
        <h2 className="mt-2 text-sm font-bold uppercase text-navy">PHIẾU KHÁM BỆNH VÀO PHÒNG</h2>
      </header>

      <div className="my-4 border-y border-dashed border-border py-4 text-center">
        <p className="text-[11px] uppercase text-sub">Số thứ tự</p>
        <p
          className="font-mono text-7xl font-bold leading-none text-navy"
          aria-label={`Số thứ tự ${ticket.ticketNumber}`}
        >
          {ticket.ticketNumber}
        </p>
        <p className="mt-3 text-sm font-bold uppercase text-navy">
          {ticket.roomName} - {ticket.departmentName} ({ticket.location})
        </p>
        <p className="mt-1 text-xs text-sub">{ticket.doctorName}</p>
      </div>

      <dl className="space-y-2">
        <Row label="Họ và tên">
          {ticket.patientName} ({GENDER_LABEL[ticket.gender]}, {ticket.age} tuổi)
        </Row>
        <Row label="Mã BN">
          <span className="font-mono">{ticket.patientCode}</span>
        </Row>
        <Row label="Mã lượt khám">
          <span className="font-mono">{ticket.visitCode}</span>
        </Row>
        <Row label="Dịch vụ">{ticket.serviceName}</Row>
        <Row label="Thời gian tiếp nhận">{ticket.receivedAt}</Row>
        <Row label="Dự kiến vào khám">{ticket.expectedTimeRange}</Row>
      </dl>

      <div className="mt-4 flex items-center justify-between gap-4 border-t border-dashed border-border pt-4">
        <div
          className="flex h-14 flex-1 items-stretch overflow-hidden"
          role="img"
          aria-label={`Mã vạch ${ticket.barcodeValue}`}
        >
          {bars.map((width, index) => (
            <span
              key={index}
              className={index % 2 === 0 ? 'bg-ink' : 'bg-white'}
              style={{ width: `${width * 1.5}px` }}
            />
          ))}
        </div>
        <QrCode className="h-14 w-14 shrink-0 text-ink" aria-hidden="true" />
      </div>

      <footer className="mt-4 text-center text-[11px] italic text-sub">{ticket.footerNote}</footer>
    </article>
  );
}
