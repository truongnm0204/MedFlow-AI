import { useState, type FormEvent } from 'react';
import {
  AlertCircle,
  ArrowRightLeft,
  CheckCircle2,
  DoorOpen,
  History,
  Stethoscope,
  Users,
} from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { COMPATIBLE_TRANSFER_ROOMS_MOCK } from '../data/reception-mock-data';
import { useTransferVisitRoom, useVisitCompatibleRooms } from '../hooks/use-visit-room-transfer';
import {
  transferVisitRoomSchema,
  type TransferVisitRoomFormValues,
} from '../schemas/transfer-visit-room.schema';
import type {
  CompatibleExaminationRoomItemDto,
  TransferVisitRoomRequestPayload,
  TransferVisitRoomTarget,
  VisitRoomTransferHistoryItemDto,
  VisitRoomTransferResponseDto,
} from '../types/visit-room-transfer.types';

export interface TransferVisitRoomModalProps {
  open: boolean;
  target: TransferVisitRoomTarget;
  fallbackRooms?: CompatibleExaminationRoomItemDto[];
  fallbackHistory?: VisitRoomTransferHistoryItemDto[];
  onClose: () => void;
  onTransferred: (result: {
    visitCode: string;
    previousRoomCode: string;
    currentRoomCode: string;
    currentRoomName: string;
    queueNumber: number | null;
    reason: string;
    response?: VisitRoomTransferResponseDto;
  }) => void;
  onSubmitOverride?: (
    visitIdOrCode: string,
    payload: TransferVisitRoomRequestPayload,
  ) => Promise<VisitRoomTransferResponseDto>;
}

type FieldErrors = Partial<Record<keyof TransferVisitRoomFormValues, string>>;

export function TransferVisitRoomModal({
  open,
  target,
  fallbackRooms = COMPATIBLE_TRANSFER_ROOMS_MOCK,
  fallbackHistory = [],
  onClose,
  onTransferred,
  onSubmitOverride,
}: TransferVisitRoomModalProps) {
  const [values, setValues] = useState<TransferVisitRoomFormValues>(() => ({
    visitIdOrCode: target.visitIdOrCode,
    targetRoomCode: '',
    reason: '',
    notes: '',
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const compatibleRoomsQuery = useVisitCompatibleRooms(values.visitIdOrCode, open);
  const transferMutation = useTransferVisitRoom();

  const rooms = compatibleRoomsQuery.data?.compatibleRooms ?? fallbackRooms;
  const transferHistory = compatibleRoomsQuery.data?.transferHistory ?? fallbackHistory;
  const isLoadingRooms = compatibleRoomsQuery.isLoading && rooms.length === 0;
  const currentRoomCode =
    compatibleRoomsQuery.data?.currentRoomCode ?? target.currentRoomCode ?? 'RM-001';
  const departmentName =
    compatibleRoomsQuery.data?.departmentName ?? target.departmentName ?? 'Khoa Tiêu hóa';
  const assignedDoctorName =
    compatibleRoomsQuery.data?.assignedDoctorName ??
    target.assignedDoctorName ??
    'BS Nguyễn Minh Đức';
  const queueNumber = compatibleRoomsQuery.data?.queueNumber ?? null;

  function handleSelectRoom(room: CompatibleExaminationRoomItemDto) {
    if (room.isCurrentRoom || !room.isCompatible || !room.isAvailable) return;
    setValues((prev) => ({ ...prev, targetRoomCode: room.roomCode }));
    setErrors((prev) => ({ ...prev, targetRoomCode: undefined }));
    setApiError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = transferVisitRoomSchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof TransferVisitRoomFormValues | undefined;
        if (field && !nextErrors[field]) {
          nextErrors[field] = issue.message;
        }
      }
      setErrors(nextErrors);
      return;
    }

    setErrors({});
    setIsSubmitting(true);

    const selectedRoom = rooms.find(
      (r) => r.roomCode.toUpperCase() === parsed.data.targetRoomCode.toUpperCase(),
    );

    const payload: TransferVisitRoomRequestPayload = {
      targetRoomId: selectedRoom?.roomId ?? null,
      targetRoomCode: parsed.data.targetRoomCode.toUpperCase(),
      reason: parsed.data.reason,
      notes: parsed.data.notes.length > 0 ? parsed.data.notes : null,
    };

    try {
      const response = onSubmitOverride
        ? await onSubmitOverride(parsed.data.visitIdOrCode, payload)
        : await transferMutation.mutateAsync({
            visitIdOrCode: parsed.data.visitIdOrCode,
            payload,
          });

      onTransferred({
        visitCode: response.visitCode,
        previousRoomCode: response.previousRoomCode,
        currentRoomCode: response.currentRoomCode,
        currentRoomName: response.currentRoomName,
        queueNumber: response.queueNumber,
        reason: response.transferReason,
        response,
      });
    } catch (error) {
      setApiError(
        getErrorMessage(error, 'Không thể điều chuyển bệnh nhân sang phòng khám đã chọn.'),
      );
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="transfer-room-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2
              id="transfer-room-modal-title"
              className="flex items-center gap-2 text-lg font-bold text-navy"
            >
              <ArrowRightLeft aria-hidden="true" className="h-5 w-5 text-primary" />
              <span>Chuyển bệnh nhân sang phòng khám tương thích (UC27)</span>
            </h2>
            <p className="mt-0.5 text-xs text-sub">
              Chỉ điều chuyển sang buồng khám lâm sàng cùng chuyên khoa, đang hoạt động và còn sức
              chứa. Số thứ tự tiếp đón được giữ nguyên.
            </p>
          </div>
          <Badge tone="emerald">Giữ nguyên STT</Badge>
        </div>

        {apiError && (
          <div
            role="alert"
            className="mt-4 flex items-center gap-2 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs font-semibold text-rose-700"
          >
            <AlertCircle aria-hidden="true" className="h-4 w-4 shrink-0" />
            <span>{apiError}</span>
          </div>
        )}

        <div className="mt-4 grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 sm:grid-cols-3">
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bệnh nhân
            </div>
            <div className="mt-0.5 text-sm font-bold text-ink">{target.patientName}</div>
            <div className="font-mono text-xs text-sub">
              {target.patientCode}
              {target.ticketNo ? ` • STT ${target.ticketNo}` : ''}
              {queueNumber !== null && !target.ticketNo ? ` • STT #${queueNumber}` : ''}
            </div>
          </div>
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Chuyên khoa / Bác sĩ
            </div>
            <div className="mt-0.5 flex items-center gap-1.5 text-sm font-semibold text-ink">
              <Stethoscope aria-hidden="true" className="h-4 w-4 text-primary" />
              <span>{assignedDoctorName}</span>
            </div>
            <div className="text-xs text-sub">{departmentName}</div>
          </div>
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Phòng khám hiện tại
            </div>
            <div className="mt-0.5 font-mono text-sm font-bold text-navy">{currentRoomCode}</div>
            <div className="text-[11px] text-sub">Sẽ chuyển sang phòng mới bên dưới</div>
          </div>
        </div>

        <div className="mt-4">
          <Input
            id="transfer-room-visit-code"
            label="Mã lượt khám (Visit Code / ID)"
            value={values.visitIdOrCode}
            error={errors.visitIdOrCode}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, visitIdOrCode: e.target.value }));
              setErrors((prev) => ({ ...prev, visitIdOrCode: undefined }));
            }}
          />
        </div>

        <div className="mt-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-slate-700">
              Danh sách buồng khám tương thích ({rooms.length})
            </span>
            <span className="text-[11px] text-sub">
              Phòng hiện tại, phòng bảo trì hoặc đã đầy sẽ bị khóa chọn
            </span>
          </div>

          {isLoadingRooms ? (
            <div
              role="status"
              className="mt-2.5 rounded-xl border border-border bg-canvas p-6 text-center text-xs text-sub"
            >
              Đang tải danh sách buồng khám tương thích...
            </div>
          ) : rooms.length === 0 ? (
            <div
              role="status"
              className="mt-2.5 rounded-xl border border-dashed border-border bg-canvas p-6 text-center text-xs text-sub"
            >
              Không tìm thấy buồng khám lâm sàng tương thích nào cùng chuyên khoa cho lượt khám này.
            </div>
          ) : (
            <div className="mt-2.5 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
              {rooms.map((room) => {
                const isSelected =
                  values.targetRoomCode.toUpperCase() === room.roomCode.toUpperCase();
                const isDisabled = room.isCurrentRoom || !room.isCompatible || !room.isAvailable;

                return (
                  <button
                    key={room.roomId}
                    type="button"
                    disabled={isDisabled}
                    aria-pressed={isSelected}
                    onClick={() => handleSelectRoom(room)}
                    className={`flex flex-col justify-between rounded-xl border p-3.5 text-left transition-colors ${
                      isSelected
                        ? 'border-primary bg-sky-50/70 ring-2 ring-primary/30'
                        : isDisabled
                          ? 'cursor-not-allowed border-border bg-canvas opacity-60'
                          : 'border-border bg-white hover:border-primary/60'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <DoorOpen aria-hidden="true" className="h-4 w-4 text-primary" />
                        <div>
                          <span className="font-mono text-xs font-bold text-navy">
                            {room.roomCode}
                          </span>
                          <div className="text-sm font-bold text-ink">{room.roomName}</div>
                        </div>
                      </div>
                      <Badge
                        tone={
                          room.isCurrentRoom
                            ? 'sky'
                            : room.isCompatible && room.isAvailable
                              ? 'emerald'
                              : !room.isCompatible
                                ? 'slate'
                                : 'rose'
                        }
                      >
                        {room.isCurrentRoom
                          ? 'Phòng hiện tại'
                          : room.isCompatible && room.isAvailable
                            ? 'Khả dụng'
                            : !room.isCompatible
                              ? 'Khác khoa'
                              : 'Không khả dụng'}
                      </Badge>
                    </div>

                    <div className="mt-2.5 flex items-center justify-between text-xs text-sub">
                      <span>
                        {room.departmentName ?? 'Khoa lâm sàng'} • {room.location ?? 'Tầng 1'}
                      </span>
                      <span className="inline-flex items-center gap-1 font-mono font-semibold text-ink">
                        <Users aria-hidden="true" className="h-3.5 w-3.5 text-sub" />
                        {room.activeVisitCount}/{room.capacity} ca
                      </span>
                    </div>

                    {room.unavailableReason && (
                      <div className="mt-1.5 text-[11px] font-medium text-rose-600">
                        {room.unavailableReason}
                      </div>
                    )}

                    {isSelected && (
                      <div className="mt-2 inline-flex items-center gap-1 text-xs font-semibold text-primary">
                        <CheckCircle2 aria-hidden="true" className="h-3.5 w-3.5" />
                        <span>Đã chọn chuyển sang phòng này</span>
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          )}

          {errors.targetRoomCode && (
            <p role="alert" className="mt-2 text-xs font-semibold text-rose-600">
              {errors.targetRoomCode}
            </p>
          )}
        </div>

        <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            id="transfer-room-reason"
            label="Lý do chuyển phòng (bắt buộc)"
            placeholder="Ví dụ: Phòng khám hiện tại đang quá tải bệnh nhân chờ"
            value={values.reason}
            error={errors.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
            }}
          />

          <Input
            id="transfer-room-notes"
            label="Ghi chú điều phối (tùy chọn)"
            placeholder="Ví dụ: Bệnh nhân ưu tiên người cao tuổi đã đồng ý"
            value={values.notes}
            error={errors.notes}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, notes: e.target.value }));
              setErrors((prev) => ({ ...prev, notes: undefined }));
            }}
          />
        </div>

        <div className="mt-5 rounded-xl border border-border bg-canvas p-3.5">
          <div className="flex items-center gap-1.5 text-xs font-bold text-navy">
            <History aria-hidden="true" className="h-4 w-4 text-primary" />
            <span>Lịch sử điều chuyển phòng ({transferHistory.length})</span>
          </div>
          {transferHistory.length === 0 ? (
            <p className="mt-2 text-xs italic text-sub">
              Chưa có lịch sử điều chuyển phòng cho lượt khám này.
            </p>
          ) : (
            <ul className="mt-2 space-y-1.5 text-xs text-sub">
              {transferHistory.map((item) => (
                <li
                  key={item.auditLogId}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-white px-3 py-2"
                >
                  <div>
                    <span className="font-mono font-bold text-navy">
                      {item.fromRoomCode ?? '—'} → {item.toRoomCode}
                    </span>
                    <span className="ml-2 text-ink">{item.reason}</span>
                  </div>
                  <span className="font-mono text-[11px] text-sub">
                    {item.transferredByName ?? item.transferredByStaffCode ?? 'Lễ tân'}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" aria-label="Hủy bỏ" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button type="submit" aria-label="Xác nhận chuyển phòng" loading={isSubmitting}>
            Xác nhận chuyển phòng
          </Button>
        </div>
      </form>
    </Modal>
  );
}
