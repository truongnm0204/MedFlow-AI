import { useState, type FormEvent } from 'react';
import { AlertCircle, CheckCircle2, DoorOpen, Stethoscope, Users } from 'lucide-react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { COMPATIBLE_EXAMINATION_ROOMS_MOCK } from '../data/reception-mock-data';
import {
  useAssignExaminationRoom,
  useCompatibleExaminationRooms,
} from '../hooks/use-visit-room-assignment';
import {
  assignExaminationRoomSchema,
  type AssignExaminationRoomFormValues,
} from '../schemas/assign-examination-room.schema';
import type {
  AssignExaminationRoomPayload,
  AssignExaminationRoomResponseDto,
  AssignRoomVisitTarget,
  CompatibleExaminationRoomItemDto,
  VisitPriority,
} from '../types/assign-examination-room.types';

export interface AssignExaminationRoomModalProps {
  open: boolean;
  target: AssignRoomVisitTarget;
  fallbackRooms?: CompatibleExaminationRoomItemDto[];
  onClose: () => void;
  onAssigned: (result: {
    visitCode: string;
    roomCode: string;
    roomName: string;
    priority: VisitPriority;
    queueCode: string | null;
    response?: AssignExaminationRoomResponseDto;
  }) => void;
  onSubmitOverride?: (
    visitIdOrCode: string,
    payload: AssignExaminationRoomPayload,
  ) => Promise<AssignExaminationRoomResponseDto>;
}

type FieldErrors = Partial<Record<keyof AssignExaminationRoomFormValues, string>>;

const PRIORITY_OPTIONS: { value: VisitPriority; label: string }[] = [
  { value: 'NORMAL', label: 'Bình thường (NORMAL)' },
  { value: 'ELDERLY', label: 'Người cao tuổi (ELDERLY)' },
  { value: 'PREGNANT', label: 'Phụ nữ mang thai (PREGNANT)' },
];

export function AssignExaminationRoomModal({
  open,
  target,
  fallbackRooms = COMPATIBLE_EXAMINATION_ROOMS_MOCK,
  onClose,
  onAssigned,
  onSubmitOverride,
}: AssignExaminationRoomModalProps) {
  const [values, setValues] = useState<AssignExaminationRoomFormValues>(() => ({
    visitIdOrCode: target.visitIdOrCode,
    roomCode: '',
    priority: target.priority ?? 'NORMAL',
    initialPriority: target.priority ?? 'NORMAL',
    priorityVerificationConfirmed: false,
    reason: '',
  }));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const compatibleRoomsQuery = useCompatibleExaminationRooms(values.visitIdOrCode, open);
  const assignMutation = useAssignExaminationRoom();

  const rooms = compatibleRoomsQuery.data?.rooms ?? fallbackRooms;
  const requiredDepartmentName =
    compatibleRoomsQuery.data?.requiredDepartmentName ?? target.departmentName ?? 'Khoa Tiêu hóa';
  const assignedDoctorName =
    compatibleRoomsQuery.data?.assignedDoctorName ??
    target.assignedDoctorName ??
    'BS Nguyễn Minh Đức';

  function handleSelectRoom(room: CompatibleExaminationRoomItemDto) {
    if (!room.isCompatible || !room.isAvailable) return;
    setValues((prev) => ({ ...prev, roomCode: room.roomCode }));
    setErrors((prev) => ({ ...prev, roomCode: undefined }));
    setApiError(null);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setApiError(null);

    const parsed = assignExaminationRoomSchema.safeParse(values);
    if (!parsed.success) {
      const nextErrors: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const field = issue.path[0] as keyof AssignExaminationRoomFormValues | undefined;
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
      (r) => r.roomCode.toUpperCase() === parsed.data.roomCode.toUpperCase(),
    );

    const payload: AssignExaminationRoomPayload = {
      examinationRoomId: selectedRoom?.id ?? null,
      roomCode: parsed.data.roomCode.toUpperCase(),
      priority: parsed.data.priority,
      reason: parsed.data.reason.length > 0 ? parsed.data.reason : null,
      priorityVerificationConfirmed: parsed.data.priorityVerificationConfirmed,
    };

    try {
      const response = onSubmitOverride
        ? await onSubmitOverride(parsed.data.visitIdOrCode, payload)
        : await assignMutation.mutateAsync({
            visitIdOrCode: parsed.data.visitIdOrCode,
            payload,
          });

      onAssigned({
        visitCode: response.visitCode,
        roomCode: response.roomCode,
        roomName: response.roomName,
        priority: response.priority ?? parsed.data.priority,
        queueCode: response.queueCode,
        response,
      });
    } catch (error) {
      setApiError(getErrorMessage(error, 'Không thể phân buồng khám cho lượt khám này.'));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="assign-room-modal-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 id="assign-room-modal-title" className="text-lg font-bold text-navy">
              Phân buồng khám lâm sàng (UC27)
            </h2>
            <p className="mt-0.5 text-xs text-sub">
              Chọn buồng khám tương thích về chuyên khoa và còn sức chứa cho lượt khám ngoại trú.
            </p>
          </div>
          <Badge tone="sky">Tiếp đón • Điều phối</Badge>
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
            <div className="font-mono text-xs text-sub">{target.patientCode}</div>
          </div>
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Bác sĩ phụ trách (UC26)
            </div>
            <div className="mt-0.5 flex items-center gap-1.5 text-sm font-semibold text-ink">
              <Stethoscope aria-hidden="true" className="h-4 w-4 text-primary" />
              <span>{assignedDoctorName}</span>
            </div>
            <div className="text-xs text-sub">{requiredDepartmentName}</div>
          </div>
          <div>
            <div className="text-[11px] font-semibold uppercase tracking-wide text-sub">
              Phòng hiện tại
            </div>
            <div className="mt-0.5 font-mono text-sm font-bold text-navy">
              {target.currentRoomCode ?? 'Chưa phân phòng'}
            </div>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            id="assign-room-visit-code"
            label="Mã lượt khám (Visit Code / ID)"
            value={values.visitIdOrCode}
            error={errors.visitIdOrCode}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, visitIdOrCode: e.target.value }));
              setErrors((prev) => ({ ...prev, visitIdOrCode: undefined }));
            }}
          />

          <div className="flex flex-col gap-2">
            <label
              htmlFor="assign-room-priority"
              className="text-xs font-semibold uppercase tracking-wide text-slate-700"
            >
              Mức độ ưu tiên
            </label>
            <select
              id="assign-room-priority"
              value={values.priority}
              onChange={(e) => {
                setValues((prev) => ({
                  ...prev,
                  priority: e.target.value as VisitPriority,
                  priorityVerificationConfirmed: false,
                }));
                setErrors((prev) => ({
                  ...prev,
                  priority: undefined,
                  priorityVerificationConfirmed: undefined,
                }));
              }}
              className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
              {PRIORITY_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
            {values.priority !== 'NORMAL' && values.priority !== values.initialPriority && (
              <div className="mt-1">
                <div className="flex items-start gap-2">
                  <input
                    type="checkbox"
                    id="assign-room-priority-verification"
                    checked={values.priorityVerificationConfirmed}
                    onChange={(e) => {
                      setValues((prev) => ({
                        ...prev,
                        priorityVerificationConfirmed: e.target.checked,
                      }));
                      setErrors((prev) => ({
                        ...prev,
                        priorityVerificationConfirmed: undefined,
                      }));
                    }}
                    className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary/40"
                  />
                  <label
                    htmlFor="assign-room-priority-verification"
                    className="text-xs font-medium text-ink leading-relaxed"
                  >
                    Tôi đã kiểm tra giấy tờ chứng minh đối tượng ưu tiên (CCCD/giấy tờ tùy thân hoặc
                    sổ khám thai)
                  </label>
                </div>
                {errors.priorityVerificationConfirmed && (
                  <p role="alert" className="mt-1 text-xs font-semibold text-rose-600">
                    {errors.priorityVerificationConfirmed}
                  </p>
                )}
              </div>
            )}
          </div>
        </div>

        <div className="mt-5">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-slate-700">
              Danh sách buồng khám lâm sàng ({rooms.length})
            </span>
            <span className="text-[11px] text-sub">
              Chỉ cho phép gán buồng khám cùng khoa phòng và còn chỗ trống
            </span>
          </div>

          <div className="mt-2.5 grid grid-cols-1 gap-2.5 sm:grid-cols-2">
            {rooms.map((room) => {
              const isSelected = values.roomCode.toUpperCase() === room.roomCode.toUpperCase();
              const isDisabled = !room.isCompatible || !room.isAvailable;

              return (
                <button
                  key={room.id}
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
                        <div className="text-sm font-bold text-ink">{room.name}</div>
                      </div>
                    </div>
                    <Badge
                      tone={
                        room.isCompatible && room.isAvailable
                          ? 'emerald'
                          : !room.isCompatible
                            ? 'slate'
                            : 'rose'
                      }
                    >
                      {room.isCompatible && room.isAvailable
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
                      {room.currentOccupancy}/{room.capacity ?? '∞'} ca
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
                      <span>Đã chọn buồng khám này</span>
                    </div>
                  )}
                </button>
              );
            })}
          </div>

          {errors.roomCode && (
            <p role="alert" className="mt-2 text-xs font-semibold text-rose-600">
              {errors.roomCode}
            </p>
          )}
        </div>

        <div className="mt-4">
          <Input
            id="assign-room-reason"
            label="Lý do / ghi chú điều phối (tùy chọn)"
            placeholder="Ví dụ: Điều phối vào buồng khám Tiêu hóa 102 ít bệnh nhân chờ"
            value={values.reason}
            error={errors.reason}
            onChange={(e) => {
              setValues((prev) => ({ ...prev, reason: e.target.value }));
              setErrors((prev) => ({ ...prev, reason: undefined }));
            }}
          />
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" aria-label="Hủy bỏ" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button type="submit" aria-label="Xác nhận phân phòng" loading={isSubmitting}>
            Xác nhận phân phòng
          </Button>
        </div>
      </form>
    </Modal>
  );
}
