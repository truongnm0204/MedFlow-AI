import type { FieldErrors, UseFormRegister, UseFormRegisterReturn } from 'react-hook-form';

import {
  formControlClass,
  FormField,
  FormSectionCard,
} from '@/shared/components/form/form-primitives';

import type { AppointmentPolicyFormValues } from '../schemas/appointment-policy.schema';

export interface AppointmentPolicyFormProps {
  register: UseFormRegister<AppointmentPolicyFormValues>;
  errors: FieldErrors<AppointmentPolicyFormValues>;
  disabled?: boolean;
}

interface NumberInputWithUnitProps {
  id: string;
  unit: string;
  min: number;
  max: number;
  disabled?: boolean;
  registration: UseFormRegisterReturn;
}

function NumberInputWithUnit({
  id,
  unit,
  min,
  max,
  disabled,
  registration,
}: NumberInputWithUnitProps) {
  return (
    <div className="relative mt-1.5 flex items-center">
      <input
        id={id}
        type="number"
        step={1}
        min={min}
        max={max}
        disabled={disabled}
        className={`${formControlClass} !mt-0 pr-16 font-mono font-semibold`}
        {...registration}
      />
      <span className="pointer-events-none absolute right-3 text-[11px] font-medium text-sub">
        {unit}
      </span>
    </div>
  );
}

export function AppointmentPolicyForm({
  register,
  errors,
  disabled = false,
}: AppointmentPolicyFormProps) {
  return (
    <div className="space-y-6">
      <FormSectionCard
        stepNumber="1"
        title="Giới hạn đặt lịch trực tuyến"
        badge="Booking Window & Limits"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-3">
          <FormField
            label="Số ngày được đặt trước tối đa"
            required
            topNote="1 – 365 ngày"
            hint="Khoảng ngày tối đa tính từ ngày hiện tại bệnh nhân được phép đặt lịch khám."
            error={errors.bookingWindowDays?.message}
          >
            <NumberInputWithUnit
              id="policy-booking-window-days"
              unit="ngày"
              min={1}
              max={365}
              disabled={disabled}
              registration={register('bookingWindowDays', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số giờ đặt trước tối thiểu"
            required
            topNote="0 – 168 giờ"
            hint="Thời gian tối thiểu trước giờ bắt đầu khung khám mới được phép đặt lịch (phải nhỏ hơn cửa sổ đặt lịch × 24 giờ)."
            error={errors.bookingLeadHours?.message}
          >
            <NumberInputWithUnit
              id="policy-booking-lead-hours"
              unit="giờ"
              min={0}
              max={168}
              disabled={disabled}
              registration={register('bookingLeadHours', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số lịch hẹn hoạt động tối đa"
            required
            topNote="1 – 20 lịch"
            hint="Số lượng lịch hẹn đang hoạt động tối đa trên mỗi hồ sơ bệnh nhân tại cùng thời điểm."
            error={errors.maxActiveBookings?.message}
          >
            <NumberInputWithUnit
              id="policy-max-active-bookings"
              unit="lịch hẹn"
              min={1}
              max={20}
              disabled={disabled}
              registration={register('maxActiveBookings', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>

      <FormSectionCard
        stepNumber="2"
        title="Quy tắc đổi lịch & hủy lịch hẹn"
        badge="Reschedule & Cancellation"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-3">
          <FormField
            label="Số giờ tối thiểu trước khi đổi lịch"
            required
            topNote="0 – 168 giờ"
            hint="Thời hạn tối thiểu trước giờ khám bệnh nhân còn được phép đổi sang khung giờ khác."
            error={errors.rescheduleMinimumHours?.message}
          >
            <NumberInputWithUnit
              id="policy-reschedule-minimum-hours"
              unit="giờ"
              min={0}
              max={168}
              disabled={disabled}
              registration={register('rescheduleMinimumHours', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số lần đổi lịch tối đa"
            required
            topNote="0 – 10 lần"
            hint="Số lần tối đa bệnh nhân được đổi khung giờ trên cùng một mã lịch hẹn (0 = không cho phép đổi)."
            error={errors.maxReschedules?.message}
          >
            <NumberInputWithUnit
              id="policy-max-reschedules"
              unit="lần"
              min={0}
              max={10}
              disabled={disabled}
              registration={register('maxReschedules', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số giờ tối thiểu trước khi hủy lịch"
            required
            topNote="0 – 168 giờ"
            hint="Thời hạn tối thiểu trước giờ khám bệnh nhân còn được phép hủy lịch trực tuyến."
            error={errors.cancelMinimumHours?.message}
          >
            <NumberInputWithUnit
              id="policy-cancel-minimum-hours"
              unit="giờ"
              min={0}
              max={168}
              disabled={disabled}
              registration={register('cancelMinimumHours', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>

      <FormSectionCard
        stepNumber="3"
        title="Kiểm soát vắng mặt & khóa đặt lịch"
        badge="No-Show Governance"
        note="Bắt buộc (*)"
      >
        <div className="grid gap-5 md:grid-cols-2">
          <FormField
            label="Số phút chờ trước khi tính vắng mặt"
            required
            topNote="0 – 120 phút"
            hint="Thời gian ân hạn sau giờ bắt đầu slot trước khi ghi nhận trạng thái vắng mặt (NO_SHOW)."
            error={errors.noShowGraceMinutes?.message}
          >
            <NumberInputWithUnit
              id="policy-no-show-grace-minutes"
              unit="phút"
              min={0}
              max={120}
              disabled={disabled}
              registration={register('noShowGraceMinutes', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số lần vắng mặt tối đa"
            required
            topNote="1 – 20 lần"
            hint="Ngưỡng số lần vắng mặt kích hoạt chế độ tạm khóa đặt lịch trực tuyến."
            error={errors.noShowMaxCount?.message}
          >
            <NumberInputWithUnit
              id="policy-no-show-max-count"
              unit="lần"
              min={1}
              max={20}
              disabled={disabled}
              registration={register('noShowMaxCount', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số ngày xét vắng mặt"
            required
            topNote="1 – 365 ngày"
            hint="Khoảng ngày tra cứu lịch sử vắng mặt tính lùi từ thời điểm đặt lịch."
            error={errors.noShowLookbackDays?.message}
          >
            <NumberInputWithUnit
              id="policy-no-show-lookback-days"
              unit="ngày"
              min={1}
              max={365}
              disabled={disabled}
              registration={register('noShowLookbackDays', { valueAsNumber: true })}
            />
          </FormField>

          <FormField
            label="Số ngày khóa đặt lịch"
            required
            topNote="0 – 365 ngày (0 = tắt)"
            hint="Số ngày tạm khóa đặt lịch trực tuyến khi hồ sơ vượt ngưỡng vắng mặt (nhập 0 để không khóa)."
            error={errors.noShowBlockDays?.message}
          >
            <NumberInputWithUnit
              id="policy-no-show-block-days"
              unit="ngày"
              min={0}
              max={365}
              disabled={disabled}
              registration={register('noShowBlockDays', { valueAsNumber: true })}
            />
          </FormField>
        </div>
      </FormSectionCard>
    </div>
  );
}
