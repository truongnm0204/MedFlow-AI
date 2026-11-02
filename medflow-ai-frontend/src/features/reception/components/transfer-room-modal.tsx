import { useState, type FormEvent } from 'react';
import { ArrowRightLeft } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { TRANSFER_REASON_OPTIONS } from '../data/transfer-room-mock';
import { transferRoomSchema } from '../schemas/transfer-room.schema';
import type { TransferQueueVisit, TransferRoomPayload } from '../types/transfer-room.types';
import { TransferHistoryList } from './transfer-history-list';

interface TransferRoomModalProps {
  visit: TransferQueueVisit | null;
  onClose: () => void;
  onSubmit: (payload: TransferRoomPayload) => void;
}

type FieldErrors = Partial<Record<'targetRoomId' | 'reasonCode' | 'note', string>>;

interface FormProps {
  visit: TransferQueueVisit;
  onClose: () => void;
  onSubmit: (payload: TransferRoomPayload) => void;
}

function TransferRoomForm({ visit, onClose, onSubmit }: FormProps) {
  const [targetRoomId, setTargetRoomId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    const result = transferRoomSchema.safeParse({
      targetRoomId,
      reasonCode: reasonCode || undefined,
      note: note.trim() === '' ? undefined : note.trim(),
    });
    if (!result.success) {
      const next: FieldErrors = {};
      for (const issue of result.error.issues) {
        const key = issue.path[0];
        if (
          (key === 'targetRoomId' || key === 'reasonCode' || key === 'note') &&
          next[key] === undefined
        ) {
          next[key] = issue.message;
        }
      }
      setErrors(next);
      return;
    }
    setErrors({});
    onSubmit({ visitCode: visit.visitCode, ...result.data });
  };

  return (
    <form
      onSubmit={handleSubmit}
      noValidate
      className="max-h-[90vh] overflow-y-auto"
      aria-label="Điều phối buồng khám"
    >
      <div className="border-b border-border p-6 pr-14">
        <h2 className="text-base font-bold uppercase text-navy">
          CHI TIẾT LƯỢT CHỜ &amp; ĐIỀU PHỐI THỨ TỰ
        </h2>
        <div className="mt-2 text-sm font-semibold text-navy">
          {visit.patientName} • {visit.gender}, {visit.age} tuổi
        </div>
        <div className="mt-1 flex flex-wrap gap-x-4 text-xs text-sub">
          <span>
            Mã BN: <span className="font-mono font-semibold">{visit.patientCode}</span>
          </span>
          <span>
            Mã lượt khám: <span className="font-mono font-semibold">{visit.visitCode}</span>
          </span>
        </div>
        <div className="mt-2 text-xs text-ink">
          {visit.currentRoom} • {visit.department}
        </div>
      </div>

      <div className="space-y-6 p-6">
        <section aria-label="Chuyển buồng khám">
          <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-sub">
            Chuyển buồng khám dự phòng (Khi buồng bận / sự cố)
          </h3>

          <fieldset className="space-y-2">
            <legend className="mb-2 text-xs font-semibold text-navy">Buồng khám đích</legend>
            {visit.compatibleRooms.map((room) => (
              <label
                key={room.id}
                className="flex cursor-pointer items-center gap-2 rounded-xl border border-border px-3 py-2 text-sm text-ink"
              >
                <input
                  type="radio"
                  name="targetRoomId"
                  value={room.id}
                  checked={targetRoomId === room.id}
                  onChange={() => setTargetRoomId(room.id)}
                />
                {room.label} ({room.location})
              </label>
            ))}
            {errors.targetRoomId && (
              <p role="alert" className="text-xs text-rose-600">
                {errors.targetRoomId}
              </p>
            )}
          </fieldset>

          <fieldset className="mt-4 space-y-2">
            <legend className="mb-2 text-xs font-semibold text-navy">Lý do chuyển buồng</legend>
            {TRANSFER_REASON_OPTIONS.map((opt) => (
              <label
                key={opt.code}
                className="flex cursor-pointer items-center gap-2 text-sm text-ink"
              >
                <input
                  type="radio"
                  name="reasonCode"
                  value={opt.code}
                  checked={reasonCode === opt.code}
                  onChange={() => setReasonCode(opt.code)}
                />
                {opt.label}
              </label>
            ))}
            {errors.reasonCode && (
              <p role="alert" className="text-xs text-rose-600">
                {errors.reasonCode}
              </p>
            )}
          </fieldset>

          <div className="mt-4">
            <label htmlFor="transfer-note" className="mb-1 block text-xs font-semibold text-navy">
              Ghi chú lý do
            </label>
            <textarea
              id="transfer-note"
              rows={3}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              className="w-full rounded-xl border border-border bg-white px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-primary"
            />
            {errors.note && (
              <p role="alert" className="text-xs text-rose-600">
                {errors.note}
              </p>
            )}
          </div>
        </section>

        <section aria-label="Lịch sử điều phối">
          <h3 className="mb-3 text-xs font-bold uppercase tracking-wider text-sub">
            Lịch sử điều phối
          </h3>
          <TransferHistoryList entries={visit.history} />
        </section>
      </div>

      <div className="flex justify-end gap-3 border-t border-border p-4">
        <Button
          type="button"
          variant="secondary"
          size="md"
          onClick={onClose}
          aria-label="Đóng / Hủy bỏ"
        >
          Đóng / Hủy bỏ
        </Button>
        <Button
          type="submit"
          variant="primary"
          size="md"
          aria-label="Lưu điều phối và cập nhật hàng đợi"
        >
          <ArrowRightLeft className="mr-2 h-4 w-4" aria-hidden="true" />
          LƯU ĐIỀU PHỐI &amp; CẬP NHẬT HÀNG ĐỢI
        </Button>
      </div>
    </form>
  );
}

export function TransferRoomModal({ visit, onClose, onSubmit }: TransferRoomModalProps) {
  return (
    <Modal open={visit !== null} onClose={onClose} maxWidth="max-w-2xl">
      {visit && <TransferRoomForm visit={visit} onClose={onClose} onSubmit={onSubmit} />}
    </Modal>
  );
}
