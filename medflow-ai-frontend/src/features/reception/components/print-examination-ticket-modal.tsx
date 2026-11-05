import axios from 'axios';
import { AlertCircle, CheckCircle2, Printer, RefreshCw, Search } from 'lucide-react';
import { useState } from 'react';

import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { useExaminationTicket, usePrintExaminationTicket } from '../hooks/use-visit-ticket';
import type { ExaminationTicketDto } from '../types/visit-ticket.types';
import { ExaminationTicketSlip } from './examination-ticket-slip';

interface PrintExaminationTicketModalProps {
  open: boolean;
  initialVisitCode: string | null;
  onClose: () => void;
  onPrinted?: (ticket: ExaminationTicketDto) => void;
}

function extractApiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as
      { detail?: string; title?: string; message?: string } | undefined;
    if (data?.detail) return data.detail;
    if (data?.message) return data.message;
    if (data?.title) return data.title;
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return 'Không thể tải hoặc in phiếu khám bệnh. Vui lòng kiểm tra lại phân công bác sĩ (UC26) và buồng khám (UC27).';
}

export function PrintExaminationTicketModal({
  open,
  initialVisitCode,
  onClose,
  onPrinted,
}: PrintExaminationTicketModalProps) {
  const [visitCodeInput, setVisitCodeInput] = useState(initialVisitCode ?? 'VIS-202610-0001');
  const [activeVisitCode, setActiveVisitCode] = useState(initialVisitCode ?? 'VIS-202610-0001');
  const [copies, setCopies] = useState(1);
  const [reason, setReason] = useState('');
  const [localError, setLocalError] = useState<string | null>(null);
  const [printSuccessMsg, setPrintSuccessMsg] = useState<string | null>(null);

  const ticketQuery = useExaminationTicket(activeVisitCode, open);
  const printMutation = usePrintExaminationTicket();

  const displayedTicket =
    printMutation.data && printMutation.data.visitCode === ticketQuery.data?.visitCode
      ? printMutation.data
      : ticketQuery.data;

  const activeError =
    localError ??
    (printMutation.error ? extractApiErrorMessage(printMutation.error) : null) ??
    (ticketQuery.error ? extractApiErrorMessage(ticketQuery.error) : null);

  const handleLookupVisit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = visitCodeInput.trim();
    if (!trimmed) {
      setLocalError('Vui lòng nhập mã lượt khám hoặc số phiếu tiếp đón.');
      return;
    }
    setLocalError(null);
    setPrintSuccessMsg(null);
    printMutation.reset();
    setActiveVisitCode(trimmed);
  };

  const handlePrint = async () => {
    setLocalError(null);
    setPrintSuccessMsg(null);

    if (copies < 1 || copies > 5) {
      setLocalError('Số bản in phải từ 1 đến 5.');
      return;
    }

    if (reason.trim().length > 250) {
      setLocalError('Ghi chú in phiếu khám không được vượt quá 250 ký tự.');
      return;
    }

    try {
      const printed = await printMutation.mutateAsync({
        idOrCode: activeVisitCode,
        request: {
          copies,
          isReprint: (displayedTicket?.printCount ?? 0) > 0,
          reason: reason.trim() || undefined,
        },
      });

      setPrintSuccessMsg(
        `Đã phát hành lệnh in phiếu khám ${printed.ticketNo} (${printed.visitCode}) — Bản in #${printed.printCount} (${printed.copies} bản).`,
      );
      onPrinted?.(printed);
    } catch {
      // Lỗi đã được hiển thị qua printMutation.error
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <div className="max-h-[90vh] overflow-y-auto p-6" aria-label="In phiếu khám bệnh ngoại trú">
        <div className="flex items-start justify-between gap-4 border-b border-border pb-4 pr-8">
          <div>
            <h2 className="text-lg font-bold text-navy">In phiếu khám bệnh ngoại trú (UC29)</h2>
            <p className="mt-0.5 text-xs text-sub">
              Kiểm tra bác sĩ phụ trách (UC26), buồng khám (UC27), số thứ tự hàng đợi và ghi nhận
              nhật ký in.
            </p>
          </div>
        </div>

        {/* Lookup Bar */}
        <form onSubmit={handleLookupVisit} className="mt-4 flex flex-col gap-2 sm:flex-row">
          <div className="flex-1">
            <Input
              aria-label="Mã lượt khám cần in phiếu"
              placeholder="Nhập mã lượt khám (VD: VIS-202610-0001 hoặc 1)..."
              value={visitCodeInput}
              onChange={(e) => setVisitCodeInput(e.target.value)}
            />
          </div>
          <Button type="submit" variant="secondary" size="md">
            <Search className="h-4 w-4" aria-hidden="true" />
            <span>Tải phiếu khám</span>
          </Button>
        </form>

        {/* Status / Error Alerts */}
        {activeError && (
          <div
            role="alert"
            className="mt-4 flex items-start gap-2.5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{activeError}</span>
          </div>
        )}

        {printSuccessMsg && (
          <div
            role="status"
            className="mt-4 flex items-start gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-xs font-semibold text-emerald-700"
          >
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <span>{printSuccessMsg}</span>
          </div>
        )}

        {/* Ticket Slip Preview */}
        <div className="mt-4">
          {ticketQuery.isLoading ? (
            <div className="rounded-2xl border border-border bg-canvas p-8 text-center text-xs font-medium text-sub">
              Đang tải thông tin phiếu khám...
            </div>
          ) : displayedTicket ? (
            <ExaminationTicketSlip ticket={displayedTicket} />
          ) : null}
        </div>

        {/* Print Options & Actions */}
        <div className="mt-5 grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
          <div>
            <label
              htmlFor="ticket-copies-input"
              className="block text-[11px] font-bold uppercase tracking-wider text-sub"
            >
              Số bản in (1–5)
            </label>
            <input
              id="ticket-copies-input"
              type="number"
              min={1}
              max={5}
              value={copies}
              onChange={(e) => setCopies(Number.parseInt(e.target.value, 10) || 1)}
              className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 font-mono text-sm font-semibold text-ink focus:border-primary focus:outline-none"
            />
          </div>
          <div className="sm:col-span-2">
            <label
              htmlFor="ticket-reason-input"
              className="block text-[11px] font-bold uppercase tracking-wider text-sub"
            >
              Ghi chú / Lý do in (tùy chọn)
            </label>
            <input
              id="ticket-reason-input"
              type="text"
              maxLength={250}
              placeholder="VD: In phiếu khám tại quầy 1 / Bệnh nhân xin in lại..."
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink focus:border-primary focus:outline-none"
            />
          </div>
        </div>

        <div className="mt-5 flex flex-wrap items-center justify-end gap-3 border-t border-border pt-4">
          <Button type="button" variant="secondary" onClick={onClose}>
            Đóng
          </Button>
          <Button
            type="button"
            variant="primary"
            loading={printMutation.isPending}
            disabled={!displayedTicket || ticketQuery.isLoading}
            onClick={handlePrint}
          >
            {displayedTicket && displayedTicket.printCount > 0 ? (
              <>
                <RefreshCw className="h-4 w-4" aria-hidden="true" />
                <span>In lại phiếu khám</span>
              </>
            ) : (
              <>
                <Printer className="h-4 w-4" aria-hidden="true" />
                <span>In phiếu khám</span>
              </>
            )}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
