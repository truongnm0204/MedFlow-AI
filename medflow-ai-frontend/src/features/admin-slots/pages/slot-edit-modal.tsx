import { zodResolver } from '@hookform/resolvers/zod';
import { AlertTriangle, Info } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router-dom';

import { FormField, formControlClass } from '@/shared/components/form/form-primitives';
import { Button } from '@/shared/ui/button';

import { SlotEditFooter, SlotEditHeader } from '../components/slot-edit-chrome';
import {
  MOCK_SLOT_DOCTORS,
  MOCK_SLOT_ROOMS,
  MOCK_SLOT_SERVICES,
  SLOT_SPECIALTIES,
} from '../data/examination-slots-mock';
import {
  examinationSlotSchema,
  type ExaminationSlotFormValues,
} from '../schemas/examination-slot.schema';
import { useExaminationSlotStore } from '../store/examination-slot.store';
import type { ServiceType, SlotStatus } from '../types/examination-slot.types';

/** SCR-ADM-SLT-03 — edit dialog layered over the slots list, matching ServiceEditModal architecture. */
export function SlotEditModal() {
  const { slotId } = useParams();
  const navigate = useNavigate();

  const slot = useExaminationSlotStore((state) =>
    state.slots.find((item) => item.id === slotId || item.code === slotId),
  );
  const updateSlot = useExaminationSlotStore((state) => state.updateSlot);

  const [message, setMessage] = useState('');

  const {
    register,
    handleSubmit,
    setValue,
    control,
    reset,
    formState: { errors },
  } = useForm<ExaminationSlotFormValues>({
    resolver: zodResolver(examinationSlotSchema),
    defaultValues: slot
      ? {
          code: slot.code,
          serviceType: slot.serviceType,
          serviceId: slot.serviceId,
          serviceName: slot.serviceName,
          serviceCode: slot.serviceCode,
          specialty: slot.specialty,
          date: slot.date,
          startTime: slot.startTime,
          endTime: slot.endTime,
          facility: 'Cơ sở Hà Nội',
          capacityMax: slot.capacityMax,
          room: slot.room,
          doctorId: slot.doctorId,
          doctorName: slot.doctorName,
          status: slot.status,
        }
      : undefined,
  });

  useEffect(() => {
    if (slot) {
      reset({
        code: slot.code,
        serviceType: slot.serviceType,
        serviceId: slot.serviceId,
        serviceName: slot.serviceName,
        serviceCode: slot.serviceCode,
        specialty: slot.specialty,
        date: slot.date,
        startTime: slot.startTime,
        endTime: slot.endTime,
        facility: 'Cơ sở Hà Nội',
        capacityMax: slot.capacityMax,
        room: slot.room,
        doctorId: slot.doctorId,
        doctorName: slot.doctorName,
        status: slot.status,
      });
    }
  }, [slot, reset]);

  const status = useWatch({ control, name: 'status' }) ?? 'OPEN';
  const serviceType = useWatch({ control, name: 'serviceType' }) ?? 'OUTPATIENT';
  const selectedServiceId = useWatch({ control, name: 'serviceId' }) ?? '';
  const selectedDoctorId = useWatch({ control, name: 'doctorId' }) ?? '';
  const capacityMax = useWatch({ control, name: 'capacityMax' }) ?? 8;

  const filteredServices = MOCK_SLOT_SERVICES.filter((s) => s.type === serviceType);

  useEffect(() => {
    const handleKeys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') navigate('/admin/slots');
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('slot-edit-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [navigate]);

  if (!slot) {
    return (
      <div className="fixed inset-0 z-50 grid place-items-center bg-slate-900/60 p-4">
        <div className="rounded-xl bg-white p-8 text-sm">
          <p>Không tìm thấy thông tin khung giờ khám minh họa.</p>
          <Button className="mt-4" onClick={() => navigate('/admin/slots')}>
            Quay lại danh sách
          </Button>
        </div>
      </div>
    );
  }

  const save = (values: ExaminationSlotFormValues) => {
    const dateParts = values.date.split('-');
    const dateDisplay = dateParts.length === 3 ? `${dateParts[2]}/${dateParts[1]}` : values.date;

    updateSlot(slot.id, {
      code: values.code,
      serviceType: values.serviceType,
      serviceId: values.serviceId,
      serviceName: values.serviceName,
      serviceCode: values.serviceCode,
      specialty: values.specialty,
      date: values.date,
      dateDisplay,
      startTime: values.startTime,
      endTime: values.endTime,
      facility: values.facility,
      capacityMax: values.capacityMax,
      room: values.room,
      doctorId: values.doctorId,
      doctorName: values.doctorName,
      status: values.status,
    });
    navigate('/admin/slots');
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto bg-slate-900/60 p-3 backdrop-blur-[2px]"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) navigate('/admin/slots');
      }}
    >
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="slot-edit-title"
        onSubmit={handleSubmit(save)}
        className="my-auto flex max-h-[calc(100vh-24px)] w-full max-w-[896px] flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl"
      >
        <SlotEditHeader slot={slot} status={status} onClose={() => navigate('/admin/slots')} />

        <div className="overflow-y-auto">
          {/* Operational notice banner */}
          <div className="mx-6 mt-5 flex items-start gap-2 rounded-lg border border-sky-200 bg-sky-50 px-4 py-3 text-[11px] leading-relaxed text-sky-900">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <p>
              <b>Lưu ý vận hành Khám Ngoại trú:</b> Cập nhật thông tin khung giờ khám sẽ tự động
              đồng bộ thời gian thực đến Cổng đặt hẹn người bệnh và Lịch làm việc Bác sĩ. Các lượt
              khám đã đặt ({slot.capacityBooked}/{slot.capacityMax} chỗ) được bảo lưu.
            </p>
          </div>

          {message && (
            <p
              role="status"
              className="mx-6 mt-3 rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-700"
            >
              {message}
            </p>
          )}

          <div className="grid gap-x-5 gap-y-4 px-6 py-5 sm:grid-cols-2">
            {/* Slot Code */}
            <FormField
              label="Mã Slot khám (Slot ID)"
              required
              hint="Định danh chuẩn hệ thống duy nhất."
            >
              <input
                readOnly
                className={`${formControlClass} bg-slate-100 font-mono font-bold text-slate-700 cursor-not-allowed`}
                {...register('code')}
              />
            </FormField>

            {/* Service Type */}
            <FormField label="Phân loại dịch vụ" required error={errors.serviceType?.message}>
              <select
                className={formControlClass}
                {...register('serviceType', {
                  onChange: (e) => {
                    const newType = e.target.value as ServiceType;
                    const matched = MOCK_SLOT_SERVICES.find((s) => s.type === newType);
                    if (matched) {
                      setValue('serviceId', matched.id, {
                        shouldDirty: true,
                        shouldValidate: true,
                      });
                      setValue('serviceName', matched.name, {
                        shouldDirty: true,
                        shouldValidate: true,
                      });
                      setValue('serviceCode', matched.code, {
                        shouldDirty: true,
                        shouldValidate: true,
                      });
                      setValue('specialty', matched.specialty, {
                        shouldDirty: true,
                        shouldValidate: true,
                      });
                    }
                  },
                })}
              >
                <option value="OUTPATIENT">Khám Ngoại trú (OUTPATIENT)</option>
                <option value="DIAGNOSTIC">Cận lâm sàng (DIAGNOSTIC)</option>
                <option value="INPATIENT">Nội trú (INPATIENT)</option>
              </select>
            </FormField>

            {/* Service */}
            <FormField label="Dịch vụ khám" required error={errors.serviceId?.message}>
              <select
                className={formControlClass}
                value={selectedServiceId}
                onChange={(e) => {
                  const found = MOCK_SLOT_SERVICES.find((s) => s.id === e.target.value);
                  if (found) {
                    setValue('serviceId', found.id, { shouldValidate: true, shouldDirty: true });
                    setValue('serviceName', found.name, {
                      shouldValidate: true,
                      shouldDirty: true,
                    });
                    setValue('serviceCode', found.code, {
                      shouldValidate: true,
                      shouldDirty: true,
                    });
                    setValue('specialty', found.specialty, {
                      shouldValidate: true,
                      shouldDirty: true,
                    });
                  }
                }}
              >
                {filteredServices.map((srv) => (
                  <option key={srv.id} value={srv.id}>
                    {srv.code} - {srv.name}
                  </option>
                ))}
              </select>
            </FormField>

            {/* Specialty */}
            <FormField label="Chuyên khoa" required error={errors.specialty?.message}>
              <select className={formControlClass} {...register('specialty')}>
                {SLOT_SPECIALTIES.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
            </FormField>

            {/* Doctor */}
            <FormField label="Bác sĩ phụ trách" required error={errors.doctorId?.message}>
              <select
                className={formControlClass}
                value={selectedDoctorId}
                onChange={(e) => {
                  const found = MOCK_SLOT_DOCTORS.find((d) => d.id === e.target.value);
                  if (found) {
                    setValue('doctorId', found.id, { shouldValidate: true, shouldDirty: true });
                    setValue('doctorName', found.name, { shouldValidate: true, shouldDirty: true });
                  } else {
                    setValue('doctorId', '', { shouldValidate: true, shouldDirty: true });
                    setValue('doctorName', '', { shouldValidate: true, shouldDirty: true });
                  }
                }}
              >
                <option value="">-- Chưa phân công bác sĩ --</option>
                {MOCK_SLOT_DOCTORS.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name} ({item.specialty})
                  </option>
                ))}
              </select>
            </FormField>

            {/* Room */}
            <FormField label="Phòng khám / Buồng khám" required error={errors.room?.message}>
              <select className={formControlClass} {...register('room')}>
                {MOCK_SLOT_ROOMS.map((rm) => (
                  <option key={rm.id} value={rm.code}>
                    {rm.name}
                  </option>
                ))}
              </select>
            </FormField>

            {/* Date */}
            <FormField label="Ngày áp dụng (YYYY-MM-DD)" required error={errors.date?.message}>
              <input type="date" className={formControlClass} {...register('date')} />
            </FormField>

            {/* Time Slot Range */}
            <div>
              <div className="grid grid-cols-2 gap-2">
                <FormField label="Giờ bắt đầu" required error={errors.startTime?.message}>
                  <input type="time" className={formControlClass} {...register('startTime')} />
                </FormField>
                <FormField label="Giờ kết thúc" required error={errors.endTime?.message}>
                  <input type="time" className={formControlClass} {...register('endTime')} />
                </FormField>
              </div>
            </div>

            {/* Capacity */}
            <FormField
              label="Sức chứa tiếp nhận tối đa"
              required
              error={errors.capacityMax?.message}
              hint={`Đã tiếp nhận ${slot.capacityBooked}/${slot.capacityMax} lượt khám.`}
            >
              <div className="flex items-center gap-2">
                <input
                  type="number"
                  min={Math.max(1, slot.capacityBooked)}
                  max={100}
                  className={formControlClass}
                  {...register('capacityMax', { valueAsNumber: true })}
                />
                <div className="flex shrink-0 gap-1 pt-1.5">
                  {[4, 6, 8, 10, 12].map((preset) => (
                    <button
                      key={preset}
                      type="button"
                      onClick={() =>
                        setValue('capacityMax', preset, {
                          shouldDirty: true,
                          shouldValidate: true,
                        })
                      }
                      className={`rounded border px-2 py-1 text-[11px] font-semibold transition-colors cursor-pointer ${
                        capacityMax === preset
                          ? 'border-primary bg-sky-50 text-primary'
                          : 'border-slate-200 text-slate-600 hover:bg-slate-50'
                      }`}
                    >
                      {preset}
                    </button>
                  ))}
                </div>
              </div>
            </FormField>

            {/* Status */}
            <FormField label="Trạng thái khung giờ" required error={errors.status?.message}>
              <select className={formControlClass} {...register('status')}>
                <option value="OPEN">Đang mở (OPEN)</option>
                <option value="FULL">Hết chỗ (FULL)</option>
                <option value="CLOSED">Đã đóng (CLOSED)</option>
              </select>
            </FormField>
            {/* Dependent Bookings Warning on Close */}
            {status === 'CLOSED' && slot.capacityBooked > 0 && (
              <div className="sm:col-span-2 rounded-xl border border-amber-300 bg-amber-50 p-4">
                <div className="flex items-start gap-2.5">
                  <AlertTriangle
                    className="h-5 w-5 shrink-0 text-amber-600 mt-0.5"
                    aria-hidden="true"
                  />
                  <div>
                    <h4 className="text-xs font-bold text-amber-900">
                      Cảnh báo: Khung giờ hiện có {slot.capacityBooked} lượt bệnh nhân đã đặt hẹn!
                    </h4>
                    <p className="mt-0.5 text-xs text-amber-800 leading-relaxed">
                      Khi chuyển sang trạng thái <b>Đã đóng (CLOSED)</b>, hệ thống sẽ ngừng nhận
                      lượt đặt mới. Các lượt khám đã đặt trước cần được điều phối tiếp nhận hoặc chủ
                      động liên hệ dời lịch.
                    </p>
                  </div>
                </div>
              </div>
            )}

            {/* Facility */}
            <div className="sm:col-span-2">
              <FormField label="Cơ sở y tế áp dụng" required error={errors.facility?.message}>
                <input
                  className={formControlClass}
                  placeholder="Ví dụ: Cơ sở Hà Nội..."
                  {...register('facility')}
                />
              </FormField>
            </div>
          </div>
        </div>

        <SlotEditFooter
          isOpen={status === 'OPEN'}
          onClose={() => navigate('/admin/slots')}
          onToggleStatus={() => {
            const next: SlotStatus = status === 'OPEN' ? 'CLOSED' : 'OPEN';
            setValue('status', next, { shouldDirty: true });
            setMessage(
              next === 'CLOSED'
                ? 'Khung giờ đã chuyển sang trạng thái Đã đóng (CLOSED).'
                : 'Khung giờ đã chuyển sang trạng thái Đang mở (OPEN).',
            );
          }}
        />
      </form>
    </div>
  );
}

export { SlotEditModal as ExaminationSlotEditModal };
