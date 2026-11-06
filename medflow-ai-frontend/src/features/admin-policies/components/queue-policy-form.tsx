import type { FieldErrors, UseFormRegister, UseFormRegisterReturn } from 'react-hook-form';

import { FormField, FormSectionCard } from '@/shared/components/form/form-primitives';

import type { QueuePolicyFormValues } from '../schemas/queue-policy.schema';
import { NumberInputWithUnit } from './number-input-with-unit';

export interface QueuePolicyFormProps {
  register: UseFormRegister<QueuePolicyFormValues>;
  errors: FieldErrors<QueuePolicyFormValues>;
  disabled?: boolean;
}

interface PolicySwitchFieldProps {
  id: string;
  label: string;
  description: string;
  disabled?: boolean;
  registration: UseFormRegisterReturn;
}

function PolicySwitchField({
  id,
  label,
  description,
  disabled,
  registration,
}: PolicySwitchFieldProps) {
  return (
    <label
      htmlFor={id}
      className="flex cursor-pointer items-start justify-between gap-4 rounded-lg border border-border bg-slate-50/60 p-4 transition-colors hover:bg-slate-50"
    >
      <div className="space-y-1">
        <div className="text-xs font-semibold text-ink">{label}</div>
        <p className="text-[11px] leading-relaxed text-sub">{description}</p>
      </div>
      <div className="relative mt-0.5 inline-flex shrink-0 items-center">
        <input
          id={id}
          type="checkbox"
          role="switch"
          disabled={disabled}
          className="peer sr-only"
          {...registration}
        />
        <span
          aria-hidden="true"
          className="h-6 w-11 rounded-full bg-slate-300 transition-colors after:absolute after:left-[2px] after:top-[2px] after:h-5 after:w-5 after:rounded-full after:bg-white after:shadow-xs after:transition-transform peer-checked:bg-primary peer-checked:after:translate-x-5 peer-focus-visible:ring-2 peer-focus-visible:ring-primary/20 peer-disabled:opacity-50"
        />
      </div>
    </label>
  );
}

export function QueuePolicyForm({ register, errors, disabled = false }: QueuePolicyFormProps) {
  return (
    <div className="space-y-6">
      <FormSectionCard
        stepNumber="1"
        title="Bảo vệ lịch hẹn đúng giờ"
        badge="Appointment Protection"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-2">
          <FormField
            label="Thời gian ân hạn đúng giờ"
            required
            topNote="0 – 60 phút"
            hint="Bệnh nhân có hẹn đến trễ trong khoảng này vẫn được bảo vệ khung giờ (ON_TIME_OR_GRACE); quá ngưỡng chuyển LATE."
            error={errors.graceMinutes?.message}
          >
            <NumberInputWithUnit
              id="queue-policy-grace-minutes"
              unit="phút"
              min={0}
              max={60}
              disabled={disabled}
              registration={register('graceMinutes', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>

      <FormSectionCard
        stepNumber="2"
        title="Safe Backfill cho bệnh nhân đến sớm"
        badge="Safe Backfill"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-2">
          <FormField
            label="Thời lượng khám dự kiến"
            required
            topNote="5 – 120 phút"
            hint="Dùng cho công thức Safe Backfill: giờ hiện tại + thời lượng khám + dự phòng ≤ lịch hẹn được bảo vệ kế tiếp."
            error={errors.expectedDurationMinutes?.message}
          >
            <NumberInputWithUnit
              id="queue-policy-expected-duration"
              unit="phút"
              min={5}
              max={120}
              disabled={disabled}
              registration={register('expectedDurationMinutes', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Thời gian dự phòng"
            required
            topNote="0 – 30 phút"
            hint="Khoảng đệm cộng thêm để tránh lấn sang lịch hẹn được bảo vệ."
            error={errors.safetyBufferMinutes?.message}
          >
            <NumberInputWithUnit
              id="queue-policy-safety-buffer"
              unit="phút"
              min={0}
              max={30}
              disabled={disabled}
              registration={register('safetyBufferMinutes', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>

      <FormSectionCard
        stepNumber="3"
        title="Công bằng & chống chờ quá lâu"
        badge="Fairness"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-2">
          <FormField
            label="Số lượt ưu tiên liên tiếp"
            required
            topNote="1 – 10 lượt"
            hint="Số lượt nhóm ưu tiên/khám lại được phục vụ liên tiếp trước khi phải xen lượt thường."
            error={errors.walkinPriorityRatio?.priority?.message}
          >
            <NumberInputWithUnit
              id="queue-policy-ratio-priority"
              unit="lượt"
              min={1}
              max={10}
              disabled={disabled}
              registration={register('walkinPriorityRatio.priority', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số lượt thường xen kẽ"
            required
            topNote="1 – 10 lượt"
            hint="Số lượt bệnh nhân thường được phục vụ sau mỗi chuỗi lượt ưu tiên (chống chờ quá lâu)."
            error={errors.walkinPriorityRatio?.normal?.message}
          >
            <NumberInputWithUnit
              id="queue-policy-ratio-normal"
              unit="lượt"
              min={1}
              max={10}
              disabled={disabled}
              registration={register('walkinPriorityRatio.normal', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>

      <FormSectionCard
        stepNumber="4"
        title="Quy tắc vận hành"
        badge="Operational Rules"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-2">
          <PolicySwitchField
            id="queue-policy-public-number-immutable"
            label="Giữ nguyên số thứ tự công khai"
            description="queueNumber giữ bất biến sau khi cấp; hệ thống chỉ tính lại thứ tự phục vụ (servingOrder)."
            disabled={disabled}
            registration={register('publicNumberImmutable')}
          />

          <PolicySwitchField
            id="queue-policy-manual-priority-verification"
            label="Bắt buộc xác minh khi gán ưu tiên"
            description="Gán ưu tiên ELDERLY/PREGNANT phải được nhân viên có thẩm quyền xác minh, ghi lý do và nhật ký kiểm toán."
            disabled={disabled}
            registration={register('manualPriorityRequiresVerification')}
          />
        </div>
      </FormSectionCard>
    </div>
  );
}
