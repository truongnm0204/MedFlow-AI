import { useEffect } from 'react';
import { Printer } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import type { ExaminationTicketData } from '../types/examination-ticket.types';
import { ExaminationTicketCard } from './examination-ticket-card';

interface ExaminationTicketModalProps {
  ticket: ExaminationTicketData | null;
  onClose: () => void;
}

const PRINT_STYLE = `@media print {
  body * { visibility: hidden !important; }
  #examination-ticket-print-area, #examination-ticket-print-area * { visibility: visible !important; }
  #examination-ticket-print-area { position: absolute; left: 0; top: 0; width: 100%; }
}`;

export function ExaminationTicketModal({ ticket, onClose }: ExaminationTicketModalProps) {
  const open = ticket !== null;

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'F4') {
        e.preventDefault();
        window.print();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-md">
      {ticket && (
        <div className="max-h-[90vh] overflow-y-auto" aria-label="Xem trước phiếu khám">
          <style>{PRINT_STYLE}</style>
          <div className="p-4 pt-12">
            <ExaminationTicketCard ticket={ticket} />
          </div>
          <div className="flex justify-end gap-3 border-t border-border p-4">
            <Button
              type="button"
              variant="secondary"
              aria-label="Đóng phiếu khám"
              onClick={onClose}
            >
              Đóng
            </Button>
            <Button type="button" aria-label="In phiếu khám (F4)" onClick={() => window.print()}>
              <Printer className="mr-2 h-4 w-4" aria-hidden="true" />
              In phiếu khám (F4)
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}
