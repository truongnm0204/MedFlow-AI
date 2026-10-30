import {
  Barcode,
  Calendar,
  CheckCircle2,
  Clock,
  FlaskConical,
  Info,
  Plus,
  Stethoscope,
  User,
  X,
} from 'lucide-react';
import { useState } from 'react';
import type { UseFormReturn } from 'react-hook-form';

import {
  MOCK_SLOT_DOCTORS,
  MOCK_SLOT_ROOMS,
  MOCK_SLOT_SERVICES,
} from '../data/examination-slots-mock';
import type { ExaminationSlotFormValues } from '../schemas/examination-slot.schema';
import type { ServiceType } from '../types/examination-slot.types';

interface SlotCreateFormProps {
  form: UseFormReturn<ExaminationSlotFormValues>;
  serviceType: ServiceType;
  onServiceTypeChange: (type: ServiceType) => void;
  selectedRoom: string;
  onRoomSelect: (roomCode: string) => void;
  onRoomClear: () => void;
  capacityMax: number;
  onCapacityQuickPick: (capacity: number) => void;
}

const CAPACITY_PRESETS = [4, 6, 8, 10];

export function SlotCreateForm({
  form,
  serviceType,
  onServiceTypeChange,
  selectedRoom,
  onRoomSelect,
  onRoomClear,
  capacityMax,
  onCapacityQuickPick,
}: SlotCreateFormProps) {
  const {
    register,
    setValue,
    watch,
    formState: { errors },
  } = form;

  const [isPickingRoom, setIsPickingRoom] = useState(false);

  const selectedServiceId = watch('serviceId');
  const selectedDate = watch('date');
  const selectedStartTime = watch('startTime');
  const selectedEndTime = watch('endTime');
  const selectedFacility = watch('facility') || 'Cơ sở Hà Nội';

  // Filter services based on serviceType
  const filteredServices = MOCK_SLOT_SERVICES.filter((s) => s.type === serviceType);

  const handleServiceChange = (serviceId: string) => {
    const found = MOCK_SLOT_SERVICES.find((s) => s.id === serviceId);
    if (found) {
      setValue('serviceId', found.id, { shouldValidate: true, shouldDirty: true });
      setValue('serviceName', found.name, { shouldValidate: true, shouldDirty: true });
      setValue('serviceCode', found.code, { shouldValidate: true, shouldDirty: true });
      setValue('specialty', found.specialty, { shouldValidate: true, shouldDirty: true });
    }
  };

  const handleDoctorChange = (doctorId: string) => {
    const found = MOCK_SLOT_DOCTORS.find((d) => d.id === doctorId);
    if (found) {
      setValue('doctorId', found.id, { shouldValidate: true, shouldDirty: true });
      setValue('doctorName', found.name, { shouldValidate: true, shouldDirty: true });
    } else {
      setValue('doctorId', '', { shouldValidate: true, shouldDirty: true });
      setValue('doctorName', '', { shouldValidate: true, shouldDirty: true });
    }
  };

  // Format date display for preview: DD/MM/YYYY
  const formattedDate = selectedDate ? selectedDate.split('-').reverse().join('/') : '27/10/2026';

  return (
    <div className="space-y-6">
      {/* ─────────────────────────────────────────────────────────────
          CARD 1: 1. THÔNG TIN CHÍNH & PHẠM VI
          ───────────────────────────────────────────────────────────── */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-xs">
        {/* Card Header */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/80 pb-4">
          <div className="flex items-center gap-2.5">
            <span className="grid h-6 w-6 place-items-center rounded-md bg-slate-100 text-xs font-bold text-ink">
              1
            </span>
            <h2 className="text-sm font-bold uppercase tracking-wider text-ink">
              1. Thông tin chính & Phạm vi
            </h2>
          </div>
          <span className="text-xs font-medium text-slate-500">Bắt buộc (*)</span>
        </div>

        <div className="mt-5 space-y-5">
          {/* Field: Mã slot */}
          <div>
            <div className="flex items-center justify-between text-xs">
              <label htmlFor="slot-code" className="font-semibold text-ink">
                Mã slot <span className="text-rose-500">*</span>
              </label>
              <span className="text-slate-400">Mã slot do hệ thống sinh</span>
            </div>
            <div className="relative mt-1.5 flex items-center">
              <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-slate-400">
                <Barcode className="h-4 w-4" aria-hidden="true" />
              </div>
              <input
                id="slot-code"
                type="text"
                readOnly
                {...register('code')}
                className="w-full rounded-lg border border-border bg-slate-50/80 py-2.5 pl-9 pr-10 text-xs font-medium text-ink shadow-2xs focus:outline-hidden"
              />
              <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 text-sky-600">
                <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              </div>
            </div>
            <p className="mt-1.5 flex items-center gap-1.5 text-xs text-slate-500">
              <Info className="h-3.5 w-3.5 text-slate-400 shrink-0" aria-hidden="true" />
              <span>Mã slot công khai không đổi khi gọi lại.</span>
            </p>
          </div>

          {/* Field: Loại dịch vụ */}
          <div>
            <label className="text-xs font-semibold text-ink">
              Loại dịch vụ <span className="text-rose-500">*</span>
            </label>
            <div className="mt-2 grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={() => onServiceTypeChange('OUTPATIENT')}
                className={`flex items-center justify-center gap-2 rounded-lg border py-2.5 px-4 text-xs font-semibold transition-all cursor-pointer ${
                  serviceType === 'OUTPATIENT'
                    ? 'border-sky-300 bg-sky-50/70 text-primary shadow-xs ring-1 ring-sky-300'
                    : 'border-border bg-slate-50 text-sub hover:bg-slate-100 hover:text-ink'
                }`}
              >
                <Stethoscope className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span>Khám ngoại trú</span>
              </button>

              <button
                type="button"
                onClick={() => onServiceTypeChange('DIAGNOSTIC')}
                className={`flex items-center justify-center gap-2 rounded-lg border py-2.5 px-4 text-xs font-semibold transition-all cursor-pointer ${
                  serviceType === 'DIAGNOSTIC'
                    ? 'border-sky-300 bg-sky-50/70 text-primary shadow-xs ring-1 ring-sky-300'
                    : 'border-border bg-slate-50 text-sub hover:bg-slate-100 hover:text-ink'
                }`}
              >
                <FlaskConical className="h-4 w-4 shrink-0" aria-hidden="true" />
                <span>Kỹ thuật</span>
              </button>
            </div>
          </div>

          {/* Field: Dịch vụ */}
          <div>
            <label htmlFor="slot-service" className="text-xs font-semibold text-ink">
              Dịch vụ <span className="text-rose-500">*</span>
            </label>
            <div className="mt-1.5">
              <select
                id="slot-service"
                value={selectedServiceId}
                onChange={(e) => handleServiceChange(e.target.value)}
                className="w-full rounded-lg border border-border bg-white px-3 py-2.5 text-xs text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary cursor-pointer"
              >
                <option value="">-- Chọn dịch vụ khám --</option>
                {filteredServices.map((service) => (
                  <option key={service.id} value={service.id}>
                    {service.name} - {service.code} ({service.specialty})
                  </option>
                ))}
              </select>
            </div>
            {errors.serviceId && (
              <p className="mt-1 text-xs text-rose-500">{errors.serviceId.message}</p>
            )}
          </div>

          {/* Field: Ngày giờ áp dụng */}
          <div>
            <div className="flex items-center justify-between text-xs">
              <label className="font-semibold text-ink">
                Ngày giờ áp dụng <span className="text-rose-500">*</span>
              </label>
              <span className="text-slate-400">Tối đa 500 ký tự</span>
            </div>

            <div className="mt-1.5 grid grid-cols-1 gap-3 sm:grid-cols-3">
              {/* Date */}
              <div>
                <label htmlFor="slot-date" className="sr-only">
                  Ngày khám
                </label>
                <div className="relative">
                  <input
                    id="slot-date"
                    type="date"
                    {...register('date')}
                    className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
                  />
                </div>
                {errors.date && (
                  <p className="mt-1 text-[11px] text-rose-500">{errors.date.message}</p>
                )}
              </div>

              {/* Start Time */}
              <div>
                <label htmlFor="slot-start-time" className="sr-only">
                  Giờ bắt đầu
                </label>
                <div className="relative flex items-center">
                  <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-2.5 text-slate-400">
                    <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                  </div>
                  <input
                    id="slot-start-time"
                    type="time"
                    step="300"
                    {...register('startTime')}
                    className="w-full rounded-lg border border-border bg-white pl-8 pr-2.5 py-2 text-xs text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
                  />
                </div>
                {errors.startTime && (
                  <p className="mt-1 text-[11px] text-rose-500">{errors.startTime.message}</p>
                )}
              </div>

              {/* End Time */}
              <div>
                <label htmlFor="slot-end-time" className="sr-only">
                  Giờ kết thúc
                </label>
                <div className="relative flex items-center">
                  <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-2.5 text-slate-400">
                    <Clock className="h-3.5 w-3.5" aria-hidden="true" />
                  </div>
                  <input
                    id="slot-end-time"
                    type="time"
                    step="300"
                    {...register('endTime')}
                    className="w-full rounded-lg border border-border bg-white pl-8 pr-2.5 py-2 text-xs text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
                  />
                </div>
                {errors.endTime && (
                  <p className="mt-1 text-[11px] text-rose-500">{errors.endTime.message}</p>
                )}
              </div>
            </div>

            {/* Preview Banner for Date & Time */}
            <div className="mt-2.5 flex items-center gap-2 rounded-lg border border-border/70 bg-slate-50/80 px-3 py-2 text-xs text-slate-700">
              <Calendar className="h-4 w-4 text-sky-600 shrink-0" aria-hidden="true" />
              <span className="font-medium">
                {formattedDate} · {selectedStartTime || '08:00'}–{selectedEndTime || '08:20'} ·{' '}
                {selectedFacility}
              </span>
            </div>
          </div>
        </div>
      </section>

      {/* ─────────────────────────────────────────────────────────────
          CARD 2: 2. CẤU HÌNH VẬN HÀNH
          ───────────────────────────────────────────────────────────── */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-xs">
        {/* Card Header */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/80 pb-4">
          <div className="flex items-center gap-2.5">
            <span className="grid h-6 w-6 place-items-center rounded-md bg-slate-100 text-xs font-bold text-ink">
              2
            </span>
            <h2 className="text-sm font-bold uppercase tracking-wider text-ink">
              2. Cấu hình vận hành
            </h2>
          </div>
          <span className="text-xs font-medium text-slate-500">Theo quy trình cơ sở</span>
        </div>

        <div className="mt-5 space-y-5">
          {/* Field: Sức chứa slot */}
          <div>
            <div className="flex items-center justify-between text-xs">
              <label htmlFor="slot-capacity" className="font-semibold text-ink">
                Sức chứa slot <span className="text-rose-500">*</span>
              </label>
              <span className="text-slate-400">Giá trị / số lượng</span>
            </div>

            <div className="mt-1.5 flex flex-wrap items-center gap-4">
              <div className="relative w-36">
                <input
                  id="slot-capacity"
                  type="number"
                  min="1"
                  max="100"
                  {...register('capacityMax', { valueAsNumber: true })}
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-xs font-bold text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary"
                />
                <span className="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 text-xs text-slate-500">
                  lượt
                </span>
              </div>

              {/* Quick Preset Pills */}
              <div
                className="flex items-center gap-2"
                role="group"
                aria-label="Chọn nhanh sức chứa"
              >
                {CAPACITY_PRESETS.map((preset) => (
                  <button
                    key={preset}
                    type="button"
                    onClick={() => onCapacityQuickPick(preset)}
                    className={`rounded-md border px-2.5 py-1 text-xs font-semibold transition-colors cursor-pointer ${
                      capacityMax === preset
                        ? 'border-sky-300 bg-sky-50 text-primary ring-1 ring-sky-300'
                        : 'border-border bg-white text-sub hover:bg-slate-50 hover:text-ink'
                    }`}
                  >
                    {preset} lượt
                  </button>
                ))}
              </div>
            </div>
            {errors.capacityMax && (
              <p className="mt-1 text-xs text-rose-500">{errors.capacityMax.message}</p>
            )}
            <p className="mt-1.5 text-xs text-slate-500">
              Không thể giảm sức chứa dưới số lượt đã đặt.
            </p>
          </div>

          {/* Field: Phòng (nếu phân công) */}
          <div>
            <div className="flex items-center justify-between text-xs">
              <label className="font-semibold text-ink">Phòng (nếu phân công)</label>
              <span className="text-slate-500">
                {selectedRoom ? '1 phòng được chọn' : 'Chưa chọn phòng'}
              </span>
            </div>

            <div className="mt-2 space-y-2">
              {/* Selected Room Tag */}
              {selectedRoom ? (
                <div className="flex items-center gap-2">
                  <span className="inline-flex items-center gap-1.5 rounded-md border border-sky-200 bg-sky-50 px-3 py-1.5 text-xs font-semibold text-primary">
                    <span className="h-1.5 w-1.5 rounded-full bg-primary" aria-hidden="true" />
                    <span>{selectedRoom}</span>
                    <button
                      type="button"
                      onClick={onRoomClear}
                      className="ml-1 rounded-full p-0.5 hover:bg-sky-200/60 text-sky-700 cursor-pointer"
                      aria-label="Xóa phòng đã chọn"
                    >
                      <X className="h-3 w-3" aria-hidden="true" />
                    </button>
                  </span>
                </div>
              ) : null}

              {/* Room picker toggle */}
              {!isPickingRoom ? (
                <button
                  type="button"
                  onClick={() => setIsPickingRoom(true)}
                  className="flex items-center gap-1.5 rounded-lg border border-dashed border-sky-300 bg-sky-50/30 px-3 py-1.5 text-xs font-semibold text-primary hover:bg-sky-50 transition-colors cursor-pointer"
                >
                  <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                  <span>Chọn phòng</span>
                </button>
              ) : (
                <div className="rounded-lg border border-border bg-slate-50/70 p-3">
                  <div className="flex items-center justify-between text-xs font-medium text-ink mb-2">
                    <span>Chọn phòng khám:</span>
                    <button
                      type="button"
                      onClick={() => setIsPickingRoom(false)}
                      className="text-slate-400 hover:text-slate-600 text-[11px] cursor-pointer"
                    >
                      Đóng
                    </button>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {MOCK_SLOT_ROOMS.map((rm) => (
                      <button
                        key={rm.id}
                        type="button"
                        onClick={() => {
                          onRoomSelect(rm.name);
                          setIsPickingRoom(false);
                        }}
                        className={`rounded-md border px-2.5 py-1 text-xs font-medium transition-colors cursor-pointer ${
                          selectedRoom === rm.name
                            ? 'border-sky-300 bg-sky-50 text-primary'
                            : 'border-border bg-white text-slate-700 hover:bg-slate-100'
                        }`}
                      >
                        {rm.name}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Field: Bác sĩ (nếu phân công) */}
          <div>
            <label htmlFor="slot-doctor" className="text-xs font-semibold text-ink">
              Bác sĩ (nếu phân công)
            </label>
            <div className="relative mt-1.5">
              <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-slate-400">
                <User className="h-4 w-4" aria-hidden="true" />
              </div>
              <select
                id="slot-doctor"
                value={watch('doctorId') || ''}
                onChange={(e) => handleDoctorChange(e.target.value)}
                className="w-full rounded-lg border border-border bg-white pl-9 pr-8 py-2.5 text-xs text-ink shadow-2xs focus:border-primary focus:outline-hidden focus:ring-1 focus:ring-primary cursor-pointer"
              >
                <option value="">-- Chưa phân công bác sĩ (tùy chọn) --</option>
                {MOCK_SLOT_DOCTORS.map((doc) => (
                  <option key={doc.id} value={doc.id}>
                    {doc.name} ({doc.specialty})
                  </option>
                ))}
              </select>
            </div>
            <p className="mt-1.5 text-xs text-slate-500">
              Xung đột được kiểm tra theo bác sĩ, phòng và thời gian.
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}
